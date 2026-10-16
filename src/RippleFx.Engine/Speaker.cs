using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RippleFx.Engine
{
    /// <summary>What a voice is to play, in milliseconds of the file and the gain scale.</summary>
    public sealed record VoicePlay
    {
        /// <summary>Where in the file to begin.</summary>
        public double FromMs { get; init; }

        /// <summary>Where to stop; the file's end when null.</summary>
        public double? ToMs { get; init; }

        /// <summary>
        /// How loud, 0 to 1, on the engine's <b>cubic</b> scale: a speaker
        /// writing amplitude writes <c>Gain * Gain * Gain</c>.
        /// </summary>
        public double Gain { get; init; } = 1.0;

        /// <summary>Playback speed, 1 being as recorded.</summary>
        public double Rate { get; init; } = 1.0;

        /// <summary>Whether reaching <see cref="ToMs"/> comes round again — to <see cref="LoopFromMs"/>.</summary>
        public bool Looping { get; init; }

        public double LoopFromMs { get; init; }
    }

    /// <summary>
    /// What a voice reports back, which only it can know. Thread-safe: an audio
    /// thread may call either at any time, and the driver hears it on its next
    /// <see cref="SoundscapeDriver.Update"/>. A call after the voice was stopped,
    /// or after the driver is gone, is ignored.
    /// </summary>
    public sealed class VoiceEvents
    {
        private readonly WeakReference<Inbox> inbox;
        private readonly ulong voice;
        private readonly ulong generation;

        internal VoiceEvents(Inbox inbox, ulong voice, ulong generation)
        {
            this.inbox = new WeakReference<Inbox>(inbox);
            this.voice = voice;
            this.generation = generation;
        }

        /// <summary>
        /// The voice reached the end of what it was given — once per play that
        /// does not loop, and never after <see cref="IVoice.Stop"/>.
        /// </summary>
        public void Ended() => Post(new Report(voice, generation, isDuration: false, ms: 0));

        /// <summary>The file's length, once its decoder knows it.</summary>
        public void Duration(double ms) => Post(new Report(voice, generation, isDuration: true, ms: ms));

        private void Post(Report report)
        {
            if (inbox.TryGetTarget(out var target))
            {
                target.Post(report);
            }
        }

        internal readonly struct Report
        {
            public readonly ulong Voice;
            public readonly ulong Generation;
            public readonly bool IsDuration;
            public readonly double Ms;

            public Report(ulong voice, ulong generation, bool isDuration, double ms)
            {
                Voice = voice;
                Generation = generation;
                IsDuration = isDuration;
                Ms = ms;
            }
        }

        internal sealed class Inbox
        {
            private readonly object gate = new object();
            private List<Report> reports = new List<Report>();

            public void Post(Report report)
            {
                lock (gate)
                {
                    reports.Add(report);
                }
            }

            public List<Report> Take()
            {
                lock (gate)
                {
                    var taken = reports;
                    reports = new List<Report>();
                    return taken;
                }
            }
        }
    }

    /// <summary>One play of one file.</summary>
    public interface IVoice
    {
        /// <summary>
        /// Begins at once, replacing what the voice was playing. Called again on
        /// the same voice for a gapless bed whose file ended and comes round.
        /// </summary>
        void Play(VoicePlay request);

        /// <summary>A new gain on the same cubic scale; smooth it over a few milliseconds.</summary>
        void SetGain(double gain);

        void Pause();

        void Resume();

        /// <summary>Ends the voice for good. No <see cref="VoiceEvents.Ended"/> follows.</summary>
        void Stop();
    }

    /// <summary>The file a voice is for.</summary>
    public sealed record VoiceSource
    {
        /// <summary>The medium, by index into the manifest's media.</summary>
        public uint Media { get; init; }

        /// <summary>Its path inside the bundle.</summary>
        public string Path { get; init; } = "";

        /// <summary>The file on disk, for a campaign opened from a folder.</summary>
        public string? File { get; init; }
    }

    /// <summary>
    /// What a host's audio implements: voices, opened on request. The
    /// <see cref="SoundscapeDriver"/> decides everything else — when, how loud,
    /// from where in the file. <c>docs/speaker.md</c> is the contract.
    /// </summary>
    public interface ISpeaker
    {
        /// <summary>
        /// A voice for <paramref name="source"/>, returned once it can begin at
        /// once — or null when the file cannot be played, which is heard as
        /// silence and never retried. <paramref name="events"/> is how the voice
        /// reports its end and its length; keep it with the voice.
        /// </summary>
        IVoice? Open(VoiceSource source, VoiceRole role, VoiceEvents events);

        /// <summary>
        /// Whether the speaker can play the medium at all; a track it cannot is
        /// told to the scheduler as silence up front. Answer true when unsure.
        /// </summary>
        bool Resolves(VoiceSource source);
    }

    /// <summary>The clocks the driver and the player read: milliseconds, monotonic.</summary>
    public static class Clocks
    {
        /// <summary>Milliseconds since this clock was made, from <see cref="Stopwatch"/>.</summary>
        public static Func<ulong> Steady()
        {
            var watch = Stopwatch.StartNew();
            return () => (ulong)watch.ElapsedMilliseconds;
        }
    }
}
