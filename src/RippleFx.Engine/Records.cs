using System;
using System.Collections.Generic;

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
    }

    public sealed record PromptField(string Name, string Type, Value Default);

    /// <summary>A form the story asks the reader to fill.</summary>
    public sealed record Prompt(string Node, string Title, IReadOnlyList<PromptField> Fields);

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
