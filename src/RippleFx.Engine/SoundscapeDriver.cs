using System;
using System.Collections.Generic;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    public sealed record SoundscapeOptions
    {
        public uint MaxCues { get; init; } = Native.RIPPLE_MIXER_DEFAULT_MAX_CUES;
        public double GainStep { get; init; } = Native.RIPPLE_MIXER_DEFAULT_GAIN_STEP;
        public bool Muted { get; init; }
        /// <summary>The clock, in milliseconds; <see cref="Clocks.Steady"/> unless a test says otherwise.</summary>
        public Func<ulong>? Now { get; init; }
    }

    /// <summary>
    /// The host's half of the soundscape scheduler, written once: it forwards
    /// the session's audio commands to the engine's <see cref="Mixer"/>, ticks
    /// it from <see cref="Update"/> while anything sounds, and applies what it
    /// answers to an <see cref="ISpeaker"/> — a late voice's entry point, a
    /// loop's wrap, voices reported ended or measured, tracks with no file told
    /// as silence. A speaker only makes the sound. Calls come from one thread;
    /// <see cref="VoiceEvents"/> from any.
    /// </summary>
    public sealed class SoundscapeDriver : IDisposable
    {
        private readonly Campaign campaign;
        private readonly ISpeaker speaker;
        private readonly Mixer mixer;
        private readonly Func<ulong> clock;
        private readonly VoiceEvents.Inbox inbox = new VoiceEvents.Inbox();
        private readonly Dictionary<ulong, Slot> slots = new Dictionary<ulong, Slot>();
        private List<ulong> endedUnknown = new List<ulong>();
        private ulong generations;
        private bool muted;
        private bool paused;
        private bool disposed;

        public SoundscapeDriver(Campaign campaign, ISpeaker speaker, SoundscapeOptions? options = null)
        {
            options ??= new SoundscapeOptions();
            this.campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
            this.speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
            mixer = Mixer.Create(options.MaxCues, options.GainStep);
            clock = options.Now ?? Clocks.Steady();
            if (options.Muted)
            {
                Muted = true;
            }
        }

        /// <summary>
        /// Mutes or unmutes at once; the scheduler writes every gain as zero and
        /// remembers the rest.
        /// </summary>
        public bool Muted
        {
            get => muted;
            set
            {
                muted = value;
                Quietly(() => mixer.Muted = value);
                Update();
            }
        }

        /// <summary>The soundscape in force, by id: sounding, or waiting its turn.</summary>
        public string? Playing => Ask(() => mixer.PlayingId, null);

        /// <summary>Whether nothing sounds, waits or cues — when <see cref="Update"/> may rest.</summary>
        public bool Idle => Ask(() => mixer.IsIdle, true);

        /// <summary>How many voices the speaker holds open now.</summary>
        public int Voices => slots.Count;

        /// <summary>Obeys one of the session's audio commands.</summary>
        public void Apply(AudioCommand command)
        {
            switch (command.Kind)
            {
                case AudioKind.Play:
                    {
                        var scape = command.Soundscape;
                        if (scape == null)
                        {
                            return;
                        }
                        var unresolved = new List<uint>();
                        for (var i = 0; i < scape.Tracks.Count; i++)
                        {
                            var source = SourceOf((uint)scape.Tracks[i].Media);
                            if (source == null || !speaker.Resolves(source))
                            {
                                unresolved.Add((uint)i);
                            }
                        }
                        Run(() => mixer.Play(clock(), scape, unresolved, 0, muted));
                        break;
                    }
                case AudioKind.Stop:
                    Run(() => mixer.Stop(clock()));
                    break;
                case AudioKind.Cut:
                    Run(() => mixer.Cut(clock()));
                    break;
                case AudioKind.Cue:
                    // A cue with no file is ignored, as the editor does.
                    if (SourceOf(command.Media) == null)
                    {
                        return;
                    }
                    Run(() => mixer.Cue(clock(), command.Media, command.Volume, command.Speed));
                    break;
            }
        }

        /// <summary>
        /// What the host calls from its loop: hears what the voices reported and
        /// moves the timeline on. Every 25 ms or so is the editor's pace.
        /// </summary>
        public void Update()
        {
            Drain();
            if (Idle || paused)
            {
                return;
            }
            Run(() => mixer.Tick(clock()));
        }

        /// <summary>Holds every voice, and the timeline with them — not the story.</summary>
        public void Pause()
        {
            Run(() => mixer.Pause(clock()));
            paused = true;
        }

        public void Resume()
        {
            paused = false;
            Run(() => mixer.Resume(clock()));
        }

        /// <summary>Stops every voice the speaker holds, and the scheduler with them.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            foreach (var slot in slots.Values)
            {
                slot.Voice.Stop();
            }
            slots.Clear();
            mixer.Dispose();
        }

        private VoiceSource? SourceOf(uint media)
        {
            var path = campaign.MediaPath(media);
            return path == null ? null : new VoiceSource { Media = media, Path = path, File = campaign.MediaFile(media) };
        }

        // A failure from the mixer is the engine's (a trap, in practice): the
        // sound stops rather than the story.
        private static void Quietly(Action verb)
        {
            try
            {
                verb();
            }
            catch (RippleException)
            {
            }
        }

        private T Ask<T>(Func<T> read, T fallback)
        {
            if (disposed)
            {
                return fallback;
            }
            try
            {
                return read();
            }
            catch (RippleException)
            {
                return fallback;
            }
        }

        private void Run(Func<IReadOnlyList<VoiceCommand>> verb)
        {
            if (disposed)
            {
                return;
            }
            IReadOnlyList<VoiceCommand> commands;
            try
            {
                commands = verb();
            }
            catch (RippleException)
            {
                return;
            }
            Obey(commands);
            Drain();
        }

        private void RunQuietly(Func<IReadOnlyList<VoiceCommand>> verb)
        {
            try
            {
                Obey(verb());
            }
            catch (RippleException)
            {
            }
        }

        private void Obey(IReadOnlyList<VoiceCommand> commands)
        {
            foreach (var command in commands)
            {
                if (disposed)
                {
                    return;
                }
                switch (command.Kind)
                {
                    case VoiceKind.Open:
                        Open(command);
                        break;
                    case VoiceKind.Gain:
                        if (slots.TryGetValue(command.Voice, out var slot))
                        {
                            slot.Voice.SetGain(command.Gain);
                        }
                        break;
                    case VoiceKind.Stop:
                        Stop(command.Voice);
                        break;
                    case VoiceKind.Pause:
                        foreach (var each in slots.Values)
                        {
                            each.Voice.Pause();
                        }
                        break;
                    case VoiceKind.Resume:
                        foreach (var each in slots.Values)
                        {
                            each.Voice.Resume();
                        }
                        break;
                }
            }
        }

        private void Open(VoiceCommand open)
        {
            var id = open.Voice;
            VoicePlay Request(double from) => new VoicePlay
            {
                FromMs = from,
                ToMs = open.ToMs.HasValue ? open.ToMs.Value : (double?)null,
                Gain = open.Gain,
                Rate = open.Rate,
                Looping = open.Looping,
                LoopFromMs = open.LoopFromMs,
            };
            if (slots.TryGetValue(id, out var existing))
            {
                // A gapless bed whose file ended, given again on the same voice.
                existing.Voice.Play(Request(open.FromMs));
                return;
            }
            var source = SourceOf(open.Media);
            if (source == null)
            {
                // Not a file the scheduler was told is missing: the same silence
                // an unresolved track is, reported so the id is let go of.
                endedUnknown.Add(id);
                return;
            }
            var generation = ++generations;
            var issued = clock();
            var voice = speaker.Open(source, open.Role, new VoiceEvents(inbox, id, generation));
            if (voice == null)
            {
                // A file that would not play is heard the way one that ended is.
                endedUnknown.Add(id);
                return;
            }
            // How late the voice came is the host's to add (§9): the ramps do not
            // move; they are measured from when the track was due.
            var from = open.FromMs + (clock() - issued) * open.Rate;
            if (open.ToMs.HasValue)
            {
                double to = open.ToMs.Value;
                double loopFrom = open.LoopFromMs;
                if (open.Looping)
                {
                    // A bed has been round some number of times meanwhile.
                    var clip = to - loopFrom;
                    if (clip > 0 && from > to)
                    {
                        from = loopFrom + (from - loopFrom) % clip;
                    }
                }
                else if (from >= to)
                {
                    // Over by the time the voice came: heard, as far as the clock goes.
                    voice.Stop();
                    endedUnknown.Add(id);
                    return;
                }
            }
            voice.Play(Request(from));
            slots[id] = new Slot(voice, generation, wantsDuration: open.Role == VoiceRole.Track && !open.ToMs.HasValue);
        }

        // The slot goes before the voice is stopped, so a second stop on the same
        // tick finds nothing, and a late `Ended` from it is ignored.
        private void Stop(ulong id)
        {
            if (!slots.TryGetValue(id, out var slot))
            {
                return;
            }
            slots.Remove(id);
            slot.Voice.Stop();
        }

        // What the voices reported, and the voices the driver itself let go of,
        // told to the scheduler — after the batch that caused them, never inside it.
        private void Drain()
        {
            for (var rounds = 0; rounds < 64 && !disposed; rounds++)
            {
                var reports = inbox.Take();
                var unknown = endedUnknown;
                endedUnknown = new List<ulong>();
                if (reports.Count == 0 && unknown.Count == 0)
                {
                    return;
                }
                foreach (var id in unknown)
                {
                    RunQuietly(() => mixer.VoiceEnded(clock(), id));
                }
                foreach (var report in reports)
                {
                    if (!slots.TryGetValue(report.Voice, out var slot) || slot.Generation != report.Generation)
                    {
                        continue;
                    }
                    if (report.IsDuration)
                    {
                        if (!slot.WantsDuration || slot.DurationReported)
                        {
                            continue;
                        }
                        slot.DurationReported = true;
                        var voice = report.Voice;
                        var ms = (ulong)Math.Round(report.Ms, MidpointRounding.AwayFromZero);
                        Quietly(() => mixer.DurationKnown(voice, ms));
                    }
                    else
                    {
                        var voice = report.Voice;
                        RunQuietly(() => mixer.VoiceEnded(clock(), voice));
                    }
                }
            }
        }

        private sealed class Slot
        {
            public Slot(IVoice voice, ulong generation, bool wantsDuration)
            {
                Voice = voice;
                Generation = generation;
                WantsDuration = wantsDuration;
            }

            public IVoice Voice { get; }

            public ulong Generation { get; }

            public bool WantsDuration { get; }

            public bool DurationReported { get; set; }
        }
    }
}
