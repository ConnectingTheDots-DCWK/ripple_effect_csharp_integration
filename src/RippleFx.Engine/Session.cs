using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RippleFx.Engine.Internal;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    public enum Untranslated : uint
    {
        /// <summary>A passage with no text in the locale says so, in <see cref="Passage.Untranslated"/>.</summary>
        Report = 0,
        /// <summary>A passage with no text in the locale shows the default locale's.</summary>
        FallBack = 1,
    }

    /// <summary>The engine's budgets and policies; <c>new SessionOptions()</c> is the engine's own.</summary>
    public sealed record SessionOptions
    {
        public SessionOptions()
        {
            RippleEngine.EnsureLoaded();
            RippleSessionOptions given = default;
            unsafe
            {
                Native.ripple_session_options_default(&given);
            }
            StepBudget = given.step_budget;
            SilentJumpLimit = given.silent_jump_limit;
            LoopLimit = given.loop_limit;
            ScriptDeadlineMs = given.script_limits.deadline_ms;
            ScriptMemoryBytes = given.script_limits.memory_bytes;
            ScriptRecursionDepth = given.script_limits.recursion_depth;
        }

        public uint StepBudget { get; init; }
        public uint SilentJumpLimit { get; init; }
        public uint LoopLimit { get; init; }
        public ulong ScriptDeadlineMs { get; init; }
        public ulong ScriptMemoryBytes { get; init; }
        public uint ScriptRecursionDepth { get; init; }
        public Untranslated Untranslated { get; init; } = Untranslated.Report;
        /// <summary>Whether every verb's batch carries the run's trace, as <see cref="Effect.Trace"/>.</summary>
        public bool Trace { get; init; }
        /// <summary>Whether the trace carries the values that flowed, not only that they did.</summary>
        public bool TracePayloads { get; init; }

        internal RippleSessionOptions Raw() => new RippleSessionOptions
        {
            step_budget = StepBudget,
            silent_jump_limit = SilentJumpLimit,
            loop_limit = LoopLimit,
            script_limits = new RippleScriptLimits
            {
                deadline_ms = ScriptDeadlineMs,
                memory_bytes = ScriptMemoryBytes,
                recursion_depth = ScriptRecursionDepth,
            },
            untranslated = (uint)Untranslated,
            trace = Trace ? (byte)1 : (byte)0,
            trace_payloads = TracePayloads ? (byte)1 : (byte)0,
        };
    }

    /// <summary>Where a new session begins.</summary>
    public sealed record SessionConfig
    {
        /// <summary>The board, by index into the manifest's boards: usually <c>Manifest.Entries[0]</c>.</summary>
        public uint Board { get; init; }
        public string? EntryNode { get; init; }
        public string? Locale { get; init; }
        /// <summary>The dice's seed: the same seed and the same moves play the same story.</summary>
        public ulong Seed { get; init; }
        public string? Digest { get; init; }
        public SessionOptions? Options { get; init; }
    }

    /// <summary>A story put down: its save — null when there was nothing to save — and what pausing did.</summary>
    public sealed record Paused(string? Save, IReadOnlyList<Effect> Effects);

    public enum CvarWrite : uint
    {
        Done = 0,
        Unknown = 1,
        Refused = 2,
    }

    public sealed record CvarChange(CvarWrite Outcome, string? Refusal, IReadOnlyList<Effect> Effects);

    public sealed record EntityRemoval(bool Removed, IReadOnlyList<Effect> Effects);

    public sealed record ConsoleResult(PythonResult Result, IReadOnlyList<Effect> Effects);

    public sealed record GuardReport(IReadOnlyList<string> Abandoned, uint Refused, bool Exhausted);

    /// <summary>The dice's state, carried across a resume by a host that wants the dice to run on.</summary>
    public sealed record Generator(uint Lo, uint Hi);

    /// <summary>
    /// One story being played. Every verb answers the effects it led to, in
    /// the order they are applied, the Turn last. One caller at a time.
    /// </summary>
    public sealed unsafe class Session : IDisposable
    {
        private readonly SessionHandle handle;

        private Session(SessionHandle handle) => this.handle = handle;

        private delegate RippleResult Call<T>(T** output, RippleError** error) where T : unmanaged;

        /// <summary>The engine's handle, for a call this binding does not wrap.</summary>
        public IntPtr DangerousHandle => handle.DangerousGetHandle();

        // A disposed handle still answers its old pointer; the engine must never see it.
        private RippleSession* Raw => handle.IsClosed
            ? throw new ObjectDisposedException(nameof(Session))
            : (RippleSession*)handle.DangerousGetHandle();

        public static Session Create(Campaign campaign, SessionConfig config)
        {
            using var scratch = new Scratch();
            var raw = new RippleSessionConfig
            {
                board = config.Board,
                entry_node = scratch.Str(config.EntryNode),
                locale = scratch.Str(config.Locale),
                seed = config.Seed,
                digest = scratch.Str(config.Digest),
                options = (config.Options ?? new SessionOptions()).Raw(),
            };
            RippleSession* session = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_session_create(campaign.Raw, &raw, &session, &error), error);
            GC.KeepAlive(campaign);
            return new Session(new SessionHandle((IntPtr)session));
        }

        /// <summary>A session picking up a save; <see cref="Start"/> it next.</summary>
        public static Session Resume(Campaign campaign, string save, ulong seed, SessionOptions? options = null)
        {
            using var scratch = new Scratch();
            var raw = (options ?? new SessionOptions()).Raw();
            RippleSession* session = null;
            RippleError* error = null;
            Check.Ok(Native.ripple_session_resume(campaign.Raw, scratch.Str(save), seed, &raw, &session, &error), error);
            GC.KeepAlive(campaign);
            return new Session(new SessionHandle((IntPtr)session));
        }

        public IReadOnlyList<Effect> Start() => Batch((o, e) => Native.ripple_session_start(Raw, o, e));

        /// <summary>Takes the choice at <paramref name="index"/> among those the passage offers.</summary>
        public IReadOnlyList<Effect> Choose(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "a choice's index is never negative; to leave the passage, Leave()");
            }
            return Batch((o, e) => Native.ripple_session_choose(Raw, (uint)index, o, e));
        }

        /// <summary>The reader leaves the passage without choosing.</summary>
        public IReadOnlyList<Effect> Leave() =>
            Batch((o, e) => Native.ripple_session_choose(Raw, Native.RIPPLE_CHOICE_NONE, o, e));

        /// <summary>
        /// Answers the form showing, field by field in the order given — or, with
        /// null, the reader leaves it.
        /// </summary>
        public IReadOnlyList<Effect> Answer(IEnumerable<KeyValuePair<string, Value>>? fields)
        {
            if (fields == null)
            {
                return Batch((o, e) => Native.ripple_session_answer(Raw, null, o, e));
            }
            using var scratch = new Scratch();
            var view = Arena.Fields(scratch, fields.ToList());
            return Batch((o, e) => Native.ripple_session_answer(Raw, view, o, e));
        }

        /// <summary>Answers a wait, once the host's clock says it is over.</summary>
        public IReadOnlyList<Effect> Elapsed() => Batch((o, e) => Native.ripple_session_elapsed(Raw, o, e));

        /// <summary>
        /// The host's clock says the shown passage's countdown begins, its
        /// <see cref="ChoiceTimer.AfterMs"/> after it was shown. Moves nothing:
        /// the effects are only the callbacks an author aimed at it. A second
        /// call, or one after the reader chose, answers none.
        /// </summary>
        /// <exception cref="HostException"><see cref="Code.NoCountdown"/> on a passage with none.</exception>
        public IReadOnlyList<Effect> BeginCountdown() => Batch((o, e) => Native.ripple_session_begin_countdown(Raw, o, e));

        /// <summary>
        /// The host's clock says the countdown ran out: the passage's
        /// <see cref="ChoiceTimer.Default"/> is taken as a reader would take it.
        /// </summary>
        /// <exception cref="HostException">
        /// <see cref="Code.CountdownNotBegun"/> before <see cref="BeginCountdown"/>;
        /// <see cref="Code.NoCountdown"/> on a passage with none.
        /// </exception>
        public IReadOnlyList<Effect> Expire() => Batch((o, e) => Native.ripple_session_expire(Raw, o, e));

        /// <summary>
        /// The reader's answer for one kind, at any time — before
        /// <see cref="Start"/> included, when it answers nothing. On a
        /// <see cref="Effect.Permit"/> turn, the answer to the last kind
        /// unanswered begins the story. Answers are not part of a save: a
        /// resumed session is told again.
        /// </summary>
        public IReadOnlyList<Effect> Permit(PermissionKind kind, bool granted) =>
            Batch((o, e) => Native.ripple_session_permit(Raw, (uint)kind, granted ? (byte)1 : (byte)0, o, e));

        /// <summary>
        /// What time it is, from the host — null for no clock. Called before
        /// every verb that can step, by a host that granted
        /// <see cref="PermissionKind.DateTime"/>; within one verb time does not move.
        /// </summary>
        public void SetNow(Moment? now)
        {
            RippleError* error = null;
            if (now == null)
            {
                Check.Ok(Native.ripple_session_set_now(Raw, null, &error), error);
            }
            else
            {
                var raw = new RippleMoment { utc_ms = now.UtcMs, offset_minutes = now.OffsetMinutes };
                Check.Ok(Native.ripple_session_set_now(Raw, &raw, &error), error);
            }
            GC.KeepAlive(this);
        }

        /// <summary>
        /// The host's last position fix — null for none. A fix handed in before
        /// the reader answered is kept, and read only if the answer is yes.
        /// </summary>
        /// <exception cref="HostException"><see cref="Code.NotPermitted"/> once location is denied.</exception>
        public void SetPosition(Position? position)
        {
            RippleError* error = null;
            if (position == null)
            {
                Check.Ok(Native.ripple_session_set_position(Raw, null, &error), error);
                GC.KeepAlive(this);
                return;
            }
            using var scratch = new Scratch();
            var raw = new RipplePosition
            {
                label = scratch.Str(position.Label),
                latitude = Optional(position.Latitude),
                longitude = Optional(position.Longitude),
                accuracy = Optional(position.Accuracy),
            };
            if (position.When is { } when)
            {
                raw.when = scratch.Array<RippleMoment>(1);
                raw.when->utc_ms = when.UtcMs;
                raw.when->offset_minutes = when.OffsetMinutes;
            }
            Check.Ok(Native.ripple_session_set_position(Raw, &raw, &error), error);
            GC.KeepAlive(this);
        }

        /// <summary>
        /// The reader's stand-in for one kind, as they typed it, answered
        /// whenever the real value is missing — null takes it away. A time is
        /// ISO 8601 (<c>2026-10-19T18:30:00+02:00</c>, or a date alone for its
        /// midnight), read at <paramref name="localOffsetMinutes"/> when typed
        /// without an offset: pass the reader's own for that date. A position is
        /// <c>"lat, lon"</c> in decimal degrees, or anything else as a place's name.
        /// </summary>
        /// <exception cref="HostException"><see cref="Code.StandIn"/>, saying why, for one that does not read.</exception>
        public void SetStandIn(PermissionKind kind, string? typed, int localOffsetMinutes)
        {
            using var scratch = new Scratch();
            RippleError* error = null;
            Check.Ok(Native.ripple_session_set_stand_in(Raw, (uint)kind, scratch.Str(typed), localOffsetMinutes, &error), error);
            GC.KeepAlive(this);
        }

        public IReadOnlyList<Effect> SetLocale(string locale)
        {
            using var scratch = new Scratch();
            var raw = scratch.Str(locale);
            return Batch((o, e) => Native.ripple_session_set_locale(Raw, raw, o, e));
        }

        public IReadOnlyList<Effect> Restart() => Batch((o, e) => Native.ripple_session_restart(Raw, o, e));

        public IReadOnlyList<Effect> Stop() => Batch((o, e) => Native.ripple_session_stop(Raw, o, e));

        /// <summary>Puts the story down: its save, and what pausing did.</summary>
        public Paused Pause() => Owned<RipplePaused, Paused>(
            (o, e) => Native.ripple_session_pause(Raw, o, e),
            p => new Paused(Read.Opt(p->save), From.Effects(p->effects, p->effect_count)));

        /// <summary>A save of the turn the story is on, without stopping it; null when there is nothing to save.</summary>
        public string? Checkpoint() => Text((o, e) => Native.ripple_session_checkpoint(Raw, o, e));

        /// <summary>Runs a line of Python in the story's interpreter, as the editor's console does.</summary>
        public ConsoleResult ConsoleFeed(string code)
        {
            using var scratch = new Scratch();
            var raw = scratch.Str(code);
            return Owned<RippleConsoleResult, ConsoleResult>(
                (o, e) => Native.ripple_session_console_feed(Raw, raw, o, e),
                r => new ConsoleResult(From.Python(&r->result), From.Effects(r->effects, r->effect_count)));
        }

        public CvarChange WriteCvar(string name, Value value)
        {
            using var scratch = new Scratch();
            var raw = scratch.Str(name);
            var view = Arena.Write(scratch, value ?? Value.Null);
            return CvarChange((o, e) => Native.ripple_session_write_cvar(Raw, raw, view, o, e));
        }

        public CvarChange ResetCvar(string name)
        {
            using var scratch = new Scratch();
            var raw = scratch.Str(name);
            return CvarChange((o, e) => Native.ripple_session_reset_cvar(Raw, raw, o, e));
        }

        public IReadOnlyList<Effect> ResetAllCvars() => Batch((o, e) => Native.ripple_session_reset_all_cvars(Raw, o, e));

        public EntityRemoval RemoveEntity(string key)
        {
            using var scratch = new Scratch();
            var raw = scratch.Str(key);
            return Owned<RippleEntityRemoval, EntityRemoval>(
                (o, e) => Native.ripple_session_remove_entity(Raw, raw, o, e),
                r => new EntityRemoval(r->removed != 0, From.Effects(r->effects, r->effect_count)));
        }

        // The readers.

        /// <summary>The board the story is on, by index into the manifest's boards.</summary>
        public uint Board
        {
            get
            {
                uint output = 0;
                RippleError* error = null;
                Check.Ok(Native.ripple_session_board(Raw, &output, &error), error);
                GC.KeepAlive(this);
                return output;
            }
        }

        public string Locale => Text((o, e) => Native.ripple_session_locale(Raw, o, e)) ?? "";

        public string DefaultLocale => Text((o, e) => Native.ripple_session_default_locale(Raw, o, e)) ?? "";

        public Ambience? Ambience => OwnedOrNull<RippleAmbience, Ambience>(
            (o, e) => Native.ripple_session_ambience(Raw, o, e), a => From.Ambience(a));

        public IReadOnlyList<Entity> Entities => Owned<RippleEntityList, IReadOnlyList<Entity>>(
            (o, e) => Native.ripple_session_entities(Raw, o, e),
            l => Read.List(l->items, l->count, item => From.Entity((RippleEntity*)item)));

        public Memory Memory => Owned<RippleMemory, Memory>(
            (o, e) => Native.ripple_session_memory(Raw, o, e), m => From.Memory(m));

        public IReadOnlyList<CvarSnapshot> Cvars => Owned<RippleCvarList, IReadOnlyList<CvarSnapshot>>(
            (o, e) => Native.ripple_session_cvars(Raw, o, e),
            l => Read.List(l->items, l->count, item => From.CvarSnapshot((RippleCvarSnapshot*)item)));

        public GuardReport Guard => Owned<RippleGuardReport, GuardReport>(
            (o, e) => Native.ripple_session_guard(Raw, o, e),
            g => new GuardReport(Read.Texts(g->abandoned, g->abandoned_count), g->refused, g->exhausted != 0));

        public bool PythonAvailable
        {
            get
            {
                bool output = false;
                RippleError* error = null;
                Check.Ok(Native.ripple_session_python_available(Raw, &output, &error), error);
                GC.KeepAlive(this);
                return output;
            }
        }

        /// <summary>Why writing <paramref name="value"/> to the cvar would be refused; null when it would not.</summary>
        public string? DescribeCvarWrite(string name, Value value)
        {
            using var scratch = new Scratch();
            var raw = scratch.Str(name);
            var view = Arena.Write(scratch, value ?? Value.Null);
            return Text((o, e) => Native.ripple_session_describe_cvar_write(Raw, raw, view, o, e));
        }

        public void SetTrace(bool enabled)
        {
            RippleError* error = null;
            Check.Ok(Native.ripple_session_set_trace(Raw, enabled ? (byte)1 : (byte)0, &error), error);
            GC.KeepAlive(this);
        }

        public void SetTracePayloads(bool enabled)
        {
            RippleError* error = null;
            Check.Ok(Native.ripple_session_set_trace_payloads(Raw, enabled ? (byte)1 : (byte)0, &error), error);
            GC.KeepAlive(this);
        }

        /// <summary>The dice's state; set it on a resumed session for the dice to run on.</summary>
        public Generator Generator
        {
            get
            {
                RippleGenerator output = default;
                RippleError* error = null;
                Check.Ok(Native.ripple_session_generator(Raw, &output, &error), error);
                GC.KeepAlive(this);
                return new Generator(output.lo, output.hi);
            }
            set
            {
                RippleError* error = null;
                Check.Ok(Native.ripple_session_set_generator(Raw, new RippleGenerator { lo = value.Lo, hi = value.Hi }, &error), error);
                GC.KeepAlive(this);
            }
        }

        public void Dispose() => handle.Dispose();

        private static RippleOptionalF64 Optional(double? value) =>
            value is { } present ? new RippleOptionalF64 { present = 1, value = present } : default;

        private IReadOnlyList<Effect> Batch(Call<RippleEffects> call) =>
            Owned<RippleEffects, IReadOnlyList<Effect>>(call, b => From.Effects(b->items, b->count));

        private CvarChange CvarChange(Call<RippleCvarChange> call) => Owned<RippleCvarChange, CvarChange>(
            call, c => new CvarChange((CvarWrite)c->outcome, Read.Opt(c->refusal), From.Effects(c->effects, c->effect_count)));

        private string? Text(Call<RippleStr> call) =>
            OwnedOrNull<RippleStr, string>(call, s => Read.Str(*s));

        private delegate TResult Reader<T, TResult>(T* owned) where T : unmanaged;

        private TResult Owned<T, TResult>(Call<T> call, Reader<T, TResult> read) where T : unmanaged where TResult : class =>
            OwnedOrNull(call, read) ?? throw new InvalidOperationException("the engine answered nothing where it promised a value");

        private TResult? OwnedOrNull<T, TResult>(Call<T> call, Reader<T, TResult> read) where T : unmanaged where TResult : class
        {
            T* output = null;
            RippleError* error = null;
            Check.Ok(call(&output, &error), error);
            try
            {
                return output == null ? null : read(output);
            }
            finally
            {
                Native.ripple_free(output);
                GC.KeepAlive(this);
            }
        }
    }

    internal sealed class SessionHandle : SafeHandle
    {
        public SessionHandle(IntPtr raw) : base(IntPtr.Zero, ownsHandle: true) => SetHandle(raw);

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override unsafe bool ReleaseHandle()
        {
            Native.ripple_session_destroy((RippleSession*)handle);
            return true;
        }
    }
}
