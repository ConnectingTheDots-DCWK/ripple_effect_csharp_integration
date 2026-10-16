using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    public enum VoiceKind : uint
    {
        Open = 0,
        Gain = 1,
        Stop = 2,
        Pause = 3,
        Resume = 4,
    }

    /// <summary>
    /// Why a voice is open: a soundscape's track, usually long and better
    /// streamed, or a cue, short and better decoded whole.
    /// </summary>
    public enum VoiceRole : uint
    {
        Track = 0,
        Cue = 1,
    }

    public enum Phase : uint
    {
        Queued = 0,
        Starting = 1,
        Playing = 2,
        Finishing = 3,
        Finished = 4,
    }

    /// <summary>One command for the speaker, read by <see cref="Kind"/>.</summary>
    public sealed record VoiceCommand
    {
        public VoiceKind Kind { get; init; } = VoiceKind.Stop;
        public ulong Voice { get; init; }
        public uint Media { get; init; }
        public ulong FromMs { get; init; }
        public ulong? ToMs { get; init; }
        public double Gain { get; init; }
        public double Rate { get; init; } = 1.0;
        public bool Looping { get; init; }
        public ulong LoopFromMs { get; init; }
        public VoiceRole Role { get; init; } = VoiceRole.Track;
        public uint Track { get; init; }
    }

    /// <summary>
    /// The engine's soundscape scheduler, raw: commands in, commands out, the
    /// host's clock on every verb. <see cref="SoundscapeDriver"/> drives one
    /// for a host that has an <see cref="ISpeaker"/>.
    /// </summary>
    public sealed unsafe class Mixer : IDisposable
    {
        private readonly MixerHandle handle;

        private Mixer(MixerHandle handle) => this.handle = handle;

        private delegate RippleResult Call(RippleVoiceCommands** output, RippleError** error);

        // A disposed handle still answers its old pointer; the engine must never see it.
        private RippleMixer* Raw => handle.IsClosed
            ? throw new ObjectDisposedException(nameof(Mixer))
            : (RippleMixer*)handle.DangerousGetHandle();

        public static Mixer Create(uint maxCues = Native.RIPPLE_MIXER_DEFAULT_MAX_CUES, double gainStep = Native.RIPPLE_MIXER_DEFAULT_GAIN_STEP)
        {
            RippleEngine.EnsureLoaded();
            RippleMixer* raw = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_mixer_create(maxCues, gainStep, &raw, &error), error);
            return new Mixer(new MixerHandle((IntPtr)raw));
        }

        /// <summary>
        /// Plays a soundscape from <paramref name="atMs"/> into it. Tracks listed
        /// in <paramref name="unresolved"/>, by index, are heard as silence.
        /// </summary>
        public IReadOnlyList<VoiceCommand> Play(ulong nowMs, Soundscape scape, IReadOnlyList<uint>? unresolved = null, ulong atMs = 0, bool muted = false)
        {
            using var scratch = new Scratch();
            var view = Write(scratch, scape);
            var count = unresolved?.Count ?? 0;
            var tracks = scratch.Array<uint>(count);
            for (var i = 0; i < count; i++)
            {
                tracks[i] = unresolved![i];
            }
            return Commands((o, e) => Native.ripple_mixer_play(Raw, nowMs, view, tracks, (UIntPtr)count, atMs, muted ? (byte)1 : (byte)0, o, e));
        }

        public IReadOnlyList<VoiceCommand> Stop(ulong nowMs) => Commands((o, e) => Native.ripple_mixer_stop(Raw, nowMs, o, e));

        public IReadOnlyList<VoiceCommand> Cut(ulong nowMs) => Commands((o, e) => Native.ripple_mixer_cut(Raw, nowMs, o, e));

        public IReadOnlyList<VoiceCommand> Cue(ulong nowMs, uint media, double volume, double speed) =>
            Commands((o, e) => Native.ripple_mixer_cue(Raw, nowMs, media, volume, speed, o, e));

        public IReadOnlyList<VoiceCommand> Pause(ulong nowMs) => Commands((o, e) => Native.ripple_mixer_pause(Raw, nowMs, o, e));

        public IReadOnlyList<VoiceCommand> Resume(ulong nowMs) => Commands((o, e) => Native.ripple_mixer_resume(Raw, nowMs, o, e));

        public IReadOnlyList<VoiceCommand> Tick(ulong nowMs) => Commands((o, e) => Native.ripple_mixer_tick(Raw, nowMs, o, e));

        public IReadOnlyList<VoiceCommand> VoiceEnded(ulong nowMs, ulong voice) =>
            Commands((o, e) => Native.ripple_mixer_voice_ended(Raw, nowMs, voice, o, e));

        public void DurationKnown(ulong voice, ulong ms)
        {
            RippleError* error = null;
            Check.Ok(Native.ripple_mixer_duration_known(Raw, voice, ms, &error), error);
            GC.KeepAlive(this);
        }

        public bool Muted
        {
            get => Flag((o, e) => Native.ripple_mixer_muted(Raw, o, e));
            set
            {
                RippleError* error = null;
                Check.Ok(Native.ripple_mixer_set_muted(Raw, value ? (byte)1 : (byte)0, &error), error);
                GC.KeepAlive(this);
            }
        }

        /// <summary>The soundscape in force, by id: sounding, or waiting its turn.</summary>
        public string? PlayingId => Id((o, e) => Native.ripple_mixer_playing_id(Raw, o, e));

        /// <summary>The soundscape fading out, by id.</summary>
        public string? FinishingId => Id((o, e) => Native.ripple_mixer_finishing_id(Raw, o, e));

        public Phase? State
        {
            get
            {
                RippleOptionalU32 output = default;
                RippleError* error = null;
                Check.Ok(Native.ripple_mixer_state(Raw, &output, &error), error);
                GC.KeepAlive(this);
                return output.present != 0 ? (Phase)output.value : (Phase?)null;
            }
        }

        public Phase TrackState(uint track)
        {
            uint output = 0;
            RippleError* error = null;
            Check.Ok(Native.ripple_mixer_track_state(Raw, track, &output, &error), error);
            GC.KeepAlive(this);
            return (Phase)output;
        }

        public ulong PositionMs
        {
            get
            {
                ulong output = 0;
                RippleError* error = null;
                Check.Ok(Native.ripple_mixer_position_ms(Raw, &output, &error), error);
                GC.KeepAlive(this);
                return output;
            }
        }

        public uint Pass
        {
            get
            {
                uint output = 0;
                RippleError* error = null;
                Check.Ok(Native.ripple_mixer_pass(Raw, &output, &error), error);
                GC.KeepAlive(this);
                return output;
            }
        }

        public bool RanOut => Flag((o, e) => Native.ripple_mixer_ran_out(Raw, o, e));

        public bool IsPaused => Flag((o, e) => Native.ripple_mixer_is_paused(Raw, o, e));

        /// <summary>Whether nothing sounds, waits or cues — when a tick may rest.</summary>
        public bool IsIdle => Flag((o, e) => Native.ripple_mixer_is_idle(Raw, o, e));

        public void Dispose() => handle.Dispose();

        /// <summary>A soundscape laid out for the engine, in <paramref name="scratch"/>.</summary>
        internal static RippleSoundscape* Write(Scratch scratch, Soundscape scape)
        {
            var tracks = scratch.Array<RippleTrack>(scape.Tracks.Count);
            for (var i = 0; i < scape.Tracks.Count; i++)
            {
                var t = scape.Tracks[i];
                tracks[i] = new RippleTrack
                {
                    media = t.Media,
                    lane = t.Lane,
                    start = t.Start,
                    clip_start = t.ClipStart,
                    clip_end = new RippleOptionalI64 { present = t.ClipEnd.HasValue ? (byte)1 : (byte)0, value = t.ClipEnd ?? 0 },
                    volume = t.Volume,
                    speed = t.Speed,
                    fade_in = t.FadeIn,
                    fade_out = t.FadeOut,
                    mode = (uint)t.Mode,
                    interval = t.Interval,
                };
            }
            var view = scratch.Array<RippleSoundscape>(1);
            view->id = scratch.Str(scape.Id);
            view->name = scratch.Str(scape.Name);
            view->length = scape.Length;
            view->looping = scape.Looping ? (byte)1 : (byte)0;
            view->fade_in = scape.FadeIn;
            view->fade_out = scape.FadeOut;
            view->volume = scape.Volume;
            view->tied = scape.Tied ? (byte)1 : (byte)0;
            view->tracks = tracks;
            view->track_count = (UIntPtr)scape.Tracks.Count;
            return view;
        }

        private delegate RippleResult FlagCall(bool* output, RippleError** error);

        private delegate RippleResult IdCall(RippleStr** output, RippleError** error);

        private bool Flag(FlagCall call)
        {
            bool output = false;
            RippleError* error = null;
            Check.Ok(call(&output, &error), error);
            GC.KeepAlive(this);
            return output;
        }

        private string? Id(IdCall call)
        {
            RippleStr* output = null;
            RippleError* error = null;
            Check.Ok(call(&output, &error), error);
            try
            {
                return output == null ? null : Read.Str(*output);
            }
            finally
            {
                Native.ripple_free(output);
                GC.KeepAlive(this);
            }
        }

        private IReadOnlyList<VoiceCommand> Commands(Call call)
        {
            RippleVoiceCommands* output = null;
            RippleError* error = null;
            Check.Ok(call(&output, &error), error);
            try
            {
                return output == null
                    ? Array.Empty<VoiceCommand>()
                    : Read.List(output->items, output->count, item => Command((RippleVoiceCommand*)item));
            }
            finally
            {
                Native.ripple_free(output);
                GC.KeepAlive(this);
            }
        }

        private static VoiceCommand Command(RippleVoiceCommand* c) => new VoiceCommand
        {
            Kind = (VoiceKind)c->kind,
            Voice = c->voice,
            Media = c->media,
            FromMs = c->from_ms,
            ToMs = Read.Opt(c->to_ms),
            Gain = c->gain,
            Rate = c->rate,
            Looping = c->looping != 0,
            LoopFromMs = c->loop_from_ms,
            Role = (VoiceRole)c->role,
            Track = c->track,
        };
    }

    internal sealed class MixerHandle : SafeHandle
    {
        public MixerHandle(IntPtr raw) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(raw);

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override unsafe bool ReleaseHandle()
        {
            Native.ripple_mixer_destroy((RippleMixer*)handle);
            return true;
        }
    }
}
