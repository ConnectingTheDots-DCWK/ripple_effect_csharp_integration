using System;
using System.Collections.Generic;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine
{
    // What the bundle holds and what a verb answers, as records the engine's
    // C structs are copied into. Nothing here points into the engine: a
    // record outlives the call that made it, the session and the campaign.
    // Times are milliseconds throughout.

    public enum TrackMode : uint
    {
        Once = 0,
        Looping = 1,
        Auto = 2,
    }

    public enum PortKind : uint
    {
        Control = 0,
        Data = 1,
    }

    /// <summary>One clip on a soundscape's timeline.</summary>
    public sealed record Track
    {
        public long Media { get; init; }
        public long Lane { get; init; }
        public long Start { get; init; }
        public long ClipStart { get; init; }
        public long? ClipEnd { get; init; }
        public double Volume { get; init; } = 1.0;
        public double Speed { get; init; } = 1.0;
        public long FadeIn { get; init; }
        public long FadeOut { get; init; }
        public TrackMode Mode { get; init; }
        public long Interval { get; init; }
    }

    /// <summary>A timeline of clips a passage plays.</summary>
    public sealed record Soundscape
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public long Length { get; init; }
        public bool Looping { get; init; }
        public long FadeIn { get; init; }
        public long FadeOut { get; init; }
        public double Volume { get; init; } = 1.0;
        public bool Tied { get; init; }
        public IReadOnlyList<Track> Tracks { get; init; } = Array.Empty<Track>();
    }

    public sealed record Snippet(string Code, bool IsInline);

    /// <summary>A row of one of the manifest's tables: a board, a passage, a medium…</summary>
    public sealed record Entry(string Id, string Name, string Path);

    public sealed record Project
    {
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string DefaultLocale { get; init; } = "";
        public IReadOnlyList<string> Locales { get; init; } = Array.Empty<string>();
        public string? Cover { get; init; }
    }

    public sealed record Cvar(string Name, string Type, Value Default);

    /// <summary><c>campaign.json</c>: what the bundle holds, table by table.</summary>
    public sealed record Manifest
    {
        public long Format { get; init; }
        public string? Generator { get; init; }
        /// <summary><c>markdown</c>, or <c>html</c>: what a passage's text is written in.</summary>
        public string TextFormat { get; init; } = "";
        public Project Project { get; init; } = new Project();
        /// <summary>The boards a story may begin on, by index into <see cref="Boards"/>.</summary>
        public IReadOnlyList<uint> Entries { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<Entry> Boards { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Passages { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Components { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Scripts { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Media { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Machines { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Entry> Soundscapes { get; init; } = Array.Empty<Entry>();
        public IReadOnlyList<Cvar> Cvars { get; init; } = Array.Empty<Cvar>();
        /// <summary>What the campaign needs of its host: <c>python</c>, when it runs scripts.</summary>
        public IReadOnlyList<string> Needs { get; init; } = Array.Empty<string>();
        /// <summary>
        /// What the story reads of the world outside it, by wire name —
        /// <c>dateTime</c>, <c>location</c> — listed by the compiler. A session
        /// asks about each before the story begins (<see cref="Effect.Permit"/>).
        /// </summary>
        public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    }

    public sealed record Edge(long Node, long Port);

    public sealed record Port(string Id, PortKind Kind, IReadOnlyList<Edge> Edges);

    public sealed record Node
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "";
        public string Title { get; init; } = "";
        public IReadOnlyList<Port> Inputs { get; init; } = Array.Empty<Port>();
        public IReadOnlyList<Port> Outputs { get; init; } = Array.Empty<Port>();
        public Value Data { get; init; } = Value.Null;
        public Value Metadata { get; init; } = Value.Null;
    }

    public sealed record Board
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public long Start { get; init; }
        public IReadOnlyList<uint> Prime { get; init; } = Array.Empty<uint>();
        public IReadOnlyList<Node> Nodes { get; init; } = Array.Empty<Node>();
    }

    public sealed record BundleProblem(string Message, string Location);

    /// <summary>
    /// One choice a passage offers. <c>Available</c> is false for a choice the
    /// author limited and the reader used up.
    /// </summary>
    public sealed record Choice(string Port, string Text, bool Available);

    public sealed record LocaleText(string Locale, string Text);

    /// <summary>A link in a passage's prose and the medium it names, by index into the manifest's media.</summary>
    public sealed record MediaLink(string Link, uint Media);

    /// <summary>
    /// A passage's countdown — <c>campaign-host-api.md</c> §4b. The host's
    /// clock waits <see cref="AfterMs"/>, calls <c>BeginCountdown</c>, waits
    /// <see cref="TimeoutMs"/> and calls <c>Expire</c>; the engine measures none
    /// of it, and takes <see cref="Default"/> as a reader would.
    /// </summary>
    public sealed record ChoiceTimer
    {
        /// <summary>How long before the countdown begins; zero is at once.</summary>
        public ulong AfterMs { get; init; }
        /// <summary>How long it runs once begun; never zero.</summary>
        public ulong TimeoutMs { get; init; }
        /// <summary>Whether the author meant the reader to see it count.</summary>
        public bool Visible { get; init; }
        /// <summary>The index into the passage's choices taken when it runs out.</summary>
        public int Default { get; init; }
    }

    /// <summary>A passage the reader is on.</summary>
    public sealed record Passage
    {
        public string Node { get; init; } = "";
        public string Title { get; init; } = "";
        /// <summary>Markdown, or HTML when the manifest's <see cref="Manifest.TextFormat"/> says so.</summary>
        public string Text { get; init; } = "";
        public IReadOnlyList<Choice> Choices { get; init; } = Array.Empty<Choice>();
        public IReadOnlyList<LocaleText>? Sources { get; init; }
        public IReadOnlyList<Snippet> Snippets { get; init; } = Array.Empty<Snippet>();
        public IReadOnlyList<MediaLink> Media { get; init; } = Array.Empty<MediaLink>();
        /// <summary>The locale this passage fell back from, when it has no translation.</summary>
        public string? Untranslated { get; init; }
        public Soundscape? Soundscape { get; init; }
        public bool Silence { get; init; }
        /// <summary>
        /// The passage's countdown; null when it waits for the reader for as
        /// long as they like. A re-rendered passage — after <c>SetLocale</c> —
        /// is the same visit, with the same countdown.
        /// </summary>
        public ChoiceTimer? Timer { get; init; }
    }

    /// <summary>One fixed answer a form field offers.</summary>
    /// <param name="Id">What a host answers with; the same in every language.</param>
    /// <param name="Label">What the reader is shown, in the reader's language.</param>
    /// <param name="Value">What the story is handed when it is picked, of the field's type.</param>
    public sealed record PromptOption(string Id, string Label, Value Value);

    /// <summary>
    /// One field of a form. <see cref="Name"/> is what an answer is keyed by;
    /// <see cref="Default"/> is what the control is filled in with, and is the
    /// answer if nobody edits it.
    /// </summary>
    public sealed record PromptField(string Name, string Type, Value Default)
    {
        /// <summary>What the reader is shown for the field, in the reader's language: the name where the author wrote nothing.</summary>
        public string Label { get; init; } = "";

        /// <summary>The hint drawn while the field is empty; empty for none. Never the answer.</summary>
        public string Placeholder { get; init; } = "";

        /// <summary>
        /// Whether the reader may leave the field empty, which answers null —
        /// leave its name out, or send <see cref="Value.Null"/>. A required field
        /// is not submitted empty.
        /// </summary>
        public bool Optional { get; init; }

        /// <summary>
        /// The fixed answers the reader picks from, in order: one for a scalar
        /// type, any number for a <c>…List</c>. Empty for a field the reader
        /// types into. Answer with the picked options' <see cref="PromptOption.Id"/>s.
        /// </summary>
        public IReadOnlyList<PromptOption> Options { get; init; } = Array.Empty<PromptOption>();
    }

    /// <summary>A form the story asks the reader to fill.</summary>
    public sealed record Prompt(string Node, string Title, IReadOnlyList<PromptField> Fields);

    /// <summary>
    /// What a story may read of the world outside it — <c>campaign-host-api.md</c>
    /// §7a, the header's <c>RipplePermissionKind</c>. Open for appending: a kind
    /// this binding does not name arrives as its number.
    /// </summary>
    public enum PermissionKind : uint
    {
        /// <summary>The date and time, <c>dateTime</c>: <see cref="Session.SetNow"/>.</summary>
        DateTime = RipplePermissionKind.RIPPLE_PERMISSION_KIND_DATE_TIME,
        /// <summary>The reader's position, <c>location</c>: <see cref="Session.SetPosition"/>.</summary>
        Location = RipplePermissionKind.RIPPLE_PERMISSION_KIND_LOCATION,
    }

    /// <summary>A <see cref="PermissionKind"/>'s wire name, as the manifest and a save spell it.</summary>
    public static class PermissionKinds
    {
        /// <summary><c>dateTime</c>, <c>location</c>; a kind this binding does not name, as its number.</summary>
        public static string Wire(this PermissionKind kind) => kind switch
        {
            PermissionKind.DateTime => "dateTime",
            PermissionKind.Location => "location",
            _ => ((uint)kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        /// <summary>The kind a wire name names, as the manifest's <see cref="Manifest.Permissions"/> lists it; null for one this binding does not know.</summary>
        public static PermissionKind? FromWire(string wire) => wire switch
        {
            "dateTime" => PermissionKind.DateTime,
            "location" => PermissionKind.Location,
            _ => null,
        };
    }

    /// <summary>
    /// An instant from the host: milliseconds since the Unix epoch, and the
    /// offset in minutes the reader's wall clock is at. A story's
    /// <c>datetime.now()</c> is naive at that offset.
    /// </summary>
    public sealed record Moment(long UtcMs, int OffsetMinutes)
    {
        /// <summary>The instant <paramref name="at"/> names, at its own offset.</summary>
        public static Moment Of(DateTimeOffset at) => new Moment(at.ToUnixTimeMilliseconds(), (int)at.Offset.TotalMinutes);

        /// <summary>This machine's clock, at this machine's offset.</summary>
        public static Moment Now() => Of(DateTimeOffset.Now);

        /// <summary>The same instant as a <see cref="DateTimeOffset"/>.</summary>
        public DateTimeOffset ToDateTimeOffset() =>
            DateTimeOffset.FromUnixTimeMilliseconds(UtcMs).ToOffset(TimeSpan.FromMinutes(OffsetMinutes));
    }

    /// <summary>
    /// A position fix, any part of which may be absent — shaped after the
    /// web's <c>GeolocationPosition</c>, as a story's <c>location_get()</c> sees it.
    /// </summary>
    public sealed record Position
    {
        /// <summary>A place's name, for a fix that has one.</summary>
        public string? Label { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        /// <summary>In metres.</summary>
        public double? Accuracy { get; init; }
        /// <summary>When the fix was taken; null when it is not known.</summary>
        public Moment? When { get; init; }
    }

    /// <summary>The soundscape in force, and the node that set it.</summary>
    public sealed record Ambience(Soundscape Soundscape, uint Board, string Node);

    public enum RunEventKind : uint
    {
        RunStarted = 0,
        NodeStarted = 1,
        NodeFinished = 2,
        MemoHit = 3,
        Diagnostic = 4,
        Log = 5,
        RunFinished = 6,
    }

    public enum PayloadKind : uint
    {
        Present = 0,
        Withheld = 1,
        Absent = 2,
    }

    public sealed record TraceValue
    {
        public string FromNode { get; init; } = "";
        public string FromPort { get; init; } = "";
        public string? ToNode { get; init; }
        public string? ToPort { get; init; }
        public PayloadKind Payload { get; init; } = PayloadKind.Absent;
        /// <summary>Set when <see cref="Payload"/> is <see cref="PayloadKind.Present"/>.</summary>
        public Value Value { get; init; } = Value.Null;
    }

    public sealed record RunDiagnostic
    {
        /// <summary>A <c>RippleRunIssue</c>.</summary>
        public uint Issue { get; init; }
        public string Message { get; init; } = "";
        public string? Node { get; init; }
        public string? Port { get; init; }
        public uint? Wires { get; init; }
    }

    /// <summary>One event of the run's trace, read by <see cref="Kind"/> as <c>RippleRunEvent</c> is.</summary>
    public sealed record RunEvent
    {
        public RunEventKind Kind { get; init; }
        public uint Run { get; init; }
        public uint Board { get; init; }
        public uint Sequence { get; init; }
        public string Node { get; init; } = "";
        public uint Step { get; init; }
        public string Entry { get; init; } = "";
        public IReadOnlyList<string> Primed { get; init; } = Array.Empty<string>();
        public string? EnteredVia { get; init; }
        public bool Pulled { get; init; }
        public IReadOnlyList<TraceValue> Inputs { get; init; } = Array.Empty<TraceValue>();
        /// <summary>A <c>RippleTurnOutcome</c>.</summary>
        public uint Outcome { get; init; }
        public IReadOnlyList<TraceValue> Outputs { get; init; } = Array.Empty<TraceValue>();
        public IReadOnlyList<string> Flowed { get; init; } = Array.Empty<string>();
        public string? Error { get; init; }
        public RunDiagnostic? Diagnostic { get; init; }
        /// <summary>A <c>RippleLogLevel</c>.</summary>
        public uint Level { get; init; }
        public string Message { get; init; } = "";
        public Value? Data { get; init; }
        public uint Steps { get; init; }
        public bool Cancelled { get; init; }
        public string? FailedNode { get; init; }
    }

    /// <summary>A named callback the author declared, fired — <c>campaign-host-api.md</c> §5b.</summary>
    public sealed record CallbackEvent
    {
        /// <summary>The name the author gave it, which a handler is registered by.</summary>
        public string Callback { get; init; } = "";
        /// <summary>A <c>RippleCallbackKind</c>; <see cref="EventName"/> spells it.</summary>
        public uint Event { get; init; }
        /// <summary>The event in the author's vocabulary: <c>cvar.changed</c>.</summary>
        public string EventName { get; init; } = "";
        public uint? Board { get; init; }
        public string? Node { get; init; }
        public string? Name { get; init; }
        public Value? From { get; init; }
        public Value? To { get; init; }
        public uint? Index { get; init; }
    }

    public sealed record Entity
    {
        public string Key { get; init; } = "";
        public string? Component { get; init; }
        public string Label { get; init; } = "";
        /// <summary>A map, field by field.</summary>
        public Value Values { get; init; } = Value.Null;
    }

    public sealed record Memory
    {
        /// <summary>A map, node by node.</summary>
        public Value Nodes { get; init; } = Value.Null;
        /// <summary>Each state machine and the state it is in.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Machines { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    }

    public sealed record CvarSnapshot(string Name, string Type, Value Default, Value Value);

    public enum AudioKind : uint
    {
        Play = 0,
        Stop = 1,
        Cut = 2,
        Cue = 3,
    }

    /// <summary>What the session asks the speaker — for <see cref="SoundscapeDriver.Apply"/>.</summary>
    public sealed record AudioCommand
    {
        public AudioKind Kind { get; init; } = AudioKind.Cut;
        /// <summary>Play: what to play.</summary>
        public Soundscape? Soundscape { get; init; }
        /// <summary>Play: the fade-in; stop: the fade-out.</summary>
        public ulong FadeMs { get; init; }
        /// <summary>Cue: the medium, by index into the manifest's media.</summary>
        public uint Media { get; init; }
        public double Volume { get; init; } = 1.0;
        public double Speed { get; init; } = 1.0;
        public string? Node { get; init; }
    }

    /// <summary>What a script came to.</summary>
    public sealed record PythonFailure
    {
        /// <summary>A <c>RipplePythonFailureKind</c>.</summary>
        public uint Kind { get; init; }
        public string? Exception { get; init; }
        public string Message { get; init; } = "";
        public uint? Line { get; init; }
        public string? Traceback { get; init; }
    }

    public sealed record PythonResult(Value Value, string Output, PythonFailure? Error);
}
