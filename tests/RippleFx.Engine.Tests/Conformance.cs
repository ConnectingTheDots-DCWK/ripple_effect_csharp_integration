// Every conformance fixture of the pinned engine release, played through this
// binding's own API — Session and Player — and compared with what the fixture
// says a reader saw, the way the release's FORMAT.md describes. The engine's
// own suites judge its rules; this judges the spelling: that every effect
// crosses into C# whole, an Int stays an Int and a Float a Float, and a host
// built on these types sees what any host sees.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace RippleFx.Engine.Tests
{
    public class Conformance
    {
        private const uint StepBudget = 65536;

        [Test]
        public void EveryFixturePlaysThroughTheSession() => PlayAll(throughPlayer: false);

        [Test]
        public void EveryFixturePlaysThroughThePlayer() => PlayAll(throughPlayer: true);

        [Test]
        public void NothingTheEngineHandedOutIsLeftOver()
        {
            PlayAll(throughPlayer: false);
            Assert.That(RippleEngine.Outstanding, Is.Zero);
        }

        private static void PlayAll(bool throughPlayer)
        {
            var python = RippleEngine.HasPython;
            var played = 0;
            var skipped = 0;
            foreach (var fixture in Fixtures.All())
            {
                var name = Path.GetFileName(fixture);
                var input = Json.Read(Path.Combine(fixture, "input.json"));
                if (NeedsPython(input) && !python)
                {
                    skipped++;
                    continue;
                }
                Compare(name, Play(fixture, input, throughPlayer), Json.Read(Path.Combine(fixture, "expect.json")));
                played++;
            }
            // Thirty-three fixtures, six of which need the interpreter.
            Assert.That(played + skipped, Is.EqualTo(33), "fixtures found");
            Assert.That(skipped, Is.EqualTo(python ? 0 : 6), "fixtures skipped for want of Python");
        }

        private static bool NeedsPython(Value input) =>
            input.Find("needs") is { Kind: ValueKind.List } needs && needs.AsList().Any(need => need.Equals(Value.Text("python")));

        private static void Compare(string name, Played got, Value wanted)
        {
            var trace = wanted.Find("trace")!.AsList();
            Assert.That(got.Trace, Has.Count.EqualTo(trace.Count), $"{name}: the trace has a different length");
            for (var i = 0; i < trace.Count; i++)
            {
                Assert.That(Json.Canonical(got.Trace[i]), Is.EqualTo(Json.Canonical(trace[i])), $"{name}: trace event {i}");
            }
            var callbacks = wanted.Find("callbacks")?.AsList() ?? Array.Empty<Value>();
            Assert.That(got.Callbacks, Has.Count.EqualTo(callbacks.Count), $"{name}: the callbacks differ in number");
            for (var i = 0; i < callbacks.Count; i++)
            {
                Assert.That(Json.Canonical(got.Callbacks[i]), Is.EqualTo(Json.Canonical(callbacks[i])), $"{name}: callback {i}");
            }
            var entities = wanted.Find("entities") ?? Value.Map();
            Assert.That(Json.Canonical(Value.Map(got.Entities)), Is.EqualTo(Json.Canonical(entities)), $"{name}: the entities");
        }

        internal static SessionOptions FixtureOptions() => new SessionOptions { StepBudget = StepBudget };

        private static Played Play(string fixture, Value input, bool throughPlayer)
        {
            using var campaign = Campaign.OpenDirectory(Path.Combine(fixture, "bundle"));
            var manifest = campaign.Manifest;
            var played = new Played();
            var record = new Recorder(manifest.Boards.Select(board => board.Id).ToArray(), campaign, played);

            var config = new SessionConfig
            {
                Board = manifest.Entries.Count == 0 ? 0 : manifest.Entries[0],
                Seed = input.Find("seed") is { } seed ? (ulong)seed.AsInt() : 0,
                Locale = input.Find("locale")?.AsText(),
                Options = FixtureOptions(),
            };
            var session = Session.Create(campaign, config);
            using IHost host = throughPlayer
                ? new PlayerHost(campaign, session, config.Seed, record)
                : new SessionHost(campaign, session, config.Seed, record);

            // What the host knows before the story starts — the instant it is
            // played at, the reader's answers and their stand-ins — told to a
            // fresh session, and again to a resumed one, which a save carries
            // no answer into. A move's answer is remembered with the rest.
            var now = input.Find("now") is { } instant
                ? Moment.Of(DateTimeOffset.Parse(instant.AsText(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
                : null;
            var answered = new List<KeyValuePair<PermissionKind, bool>>(
                ByKind(input.Find("permit")).Select(entry => new KeyValuePair<PermissionKind, bool>(entry.Key, entry.Value.AsBool())));
            var standIns = ByKind(input.Find("standIns")).Select(entry => new KeyValuePair<PermissionKind, string>(entry.Key, entry.Value.AsText())).ToArray();
            void Tell()
            {
                if (now != null)
                {
                    host.Session.SetNow(now);
                }
                foreach (var (kind, granted) in answered)
                {
                    host.Permit(kind, granted);
                }
                foreach (var (kind, typed) in standIns)
                {
                    host.Session.SetStandIn(kind, typed, 0);
                }
            }

            string? saved = null;
            Tell();
            host.Start();
            foreach (var move in input.Find("moves")!.AsList())
            {
                if (Over(host.Turn) && saved == null)
                {
                    break;
                }
                switch (move.Find("do")!.AsText())
                {
                    case "choose":
                        if (host.Turn is not Effect.Show)
                        {
                            throw new InvalidOperationException("a move chooses where no passage is showing");
                        }
                        host.Choose((int)move.Find("choice")!.AsInt());
                        break;
                    case "answer":
                        host.Answer(move.Find("values")!.AsMap());
                        break;
                    case "locale":
                        host.SetLocale(move.Find("locale")!.AsText());
                        break;
                    case "pause":
                        saved = host.Pause() ?? throw new InvalidOperationException("a move pauses where the story is not waiting");
                        break;
                    case "resume":
                        host.Resume(saved!);
                        saved = null;
                        Tell();
                        host.Start();
                        break;
                    case "restart":
                        host.Restart();
                        break;
                    // A fixture's clock runs no countdown by itself: the moves say when.
                    case "countdown":
                        AtAPassage(host, "runs a countdown");
                        host.BeginCountdown();
                        break;
                    case "expire":
                        AtAPassage(host, "runs a countdown out");
                        host.Expire();
                        break;
                    case "permit":
                        {
                            var kind = PermissionKinds.FromWire(move.Find("kind")!.AsText())
                                ?? throw new InvalidOperationException("a move answers for a kind this binding does not know");
                            var granted = move.Find("granted")!.AsBool();
                            answered.RemoveAll(entry => entry.Key == kind);
                            answered.Add(new KeyValuePair<PermissionKind, bool>(kind, granted));
                            host.Permit(kind, granted);
                            break;
                        }
                }
            }
            // The reader has left: the story goes on by itself until it is over —
            // or, nobody having answered its question, never begins.
            while (!Over(host.Turn) && host.Turn is not Effect.Permit)
            {
                switch (host.Turn)
                {
                    case Effect.Show:
                        host.Leave();
                        break;
                    case Effect.Ask:
                        host.Answer(null);
                        break;
                    default:
                        host.Elapsed();
                        break;
                }
            }
            foreach (var entity in host.Entities())
            {
                played.Entities.Add(new KeyValuePair<string, Value>(entity.Key, entity.Values));
            }
            return played;
        }

        private static bool Over(Effect? turn) => turn == null || turn.IsOver;

        private static void AtAPassage(IHost host, string what)
        {
            if (host.Turn is not Effect.Show)
            {
                throw new InvalidOperationException($"a move {what} where no passage is showing");
            }
        }

        // A fixture's map keyed by a kind's wire name, in the order written.
        private static IEnumerable<KeyValuePair<PermissionKind, Value>> ByKind(Value? map) =>
            (map?.AsMap() ?? Array.Empty<KeyValuePair<string, Value>>()).Select(entry => new KeyValuePair<PermissionKind, Value>(
                PermissionKinds.FromWire(entry.Key) ?? throw new InvalidOperationException($"\"{entry.Key}\" is not a kind a story may ask for"),
                entry.Value));

        private sealed class Played
        {
            public List<Value> Trace { get; } = new List<Value>();

            public List<Value> Callbacks { get; } = new List<Value>();

            public List<KeyValuePair<string, Value>> Entities { get; } = new List<KeyValuePair<string, Value>>();
        }

        // What a reader saw of one effect, as FORMAT.md's trace records it.
        private sealed class Recorder
        {
            private readonly IReadOnlyList<string> boards;
            private readonly Campaign campaign;
            private readonly Played into;

            public Recorder(IReadOnlyList<string> boards, Campaign campaign, Played into)
            {
                this.boards = boards;
                this.campaign = campaign;
                this.into = into;
            }

            public void Record(Effect effect)
            {
                switch (effect)
                {
                    case Effect.Show e:
                        {
                            var entries = new List<(string, Value)>
                        {
                            ("kind", "passage"),
                            ("node", e.Passage.Node),
                            ("markdown", e.Passage.Text),
                            ("choices", Value.List(e.Passage.Choices.Select(choice => Value.Text(choice.Text)))),
                        };
                            if (e.Passage.Untranslated is { } untranslated)
                            {
                                entries.Add(("untranslated", untranslated));
                            }
                            if (e.Passage.Timer is { } timer)
                            {
                                entries.Add(("timer", Value.Map(
                                    ("after", (long)timer.AfterMs),
                                    ("timeout", (long)timer.TimeoutMs),
                                    ("visible", timer.Visible),
                                    ("default", (long)timer.Default))));
                            }
                            into.Trace.Add(Value.Map(entries.ToArray()));
                            break;
                        }
                    case Effect.Ask e:
                        into.Trace.Add(Value.Map(
                            ("kind", "prompt"),
                            ("node", e.Prompt.Node),
                            ("fields", Value.List(e.Prompt.Fields.Select(Field)))));
                        break;
                    case Effect.Permit e:
                        into.Trace.Add(Value.Map(("kind", "permit"), ("kinds", Value.List(e.Kinds.Select(kind => Value.Text(kind.Wire()))))));
                        break;
                    case Effect.Notice e:
                        {
                            // `of`, because `kind` is the event's own.
                            var entries = new List<(string, Value)> { ("kind", "notice"), ("code", e.Code), ("of", e.Kind.Wire()) };
                            if (e.Node is { } node)
                            {
                                entries.Add(("node", node));
                            }
                            entries.Add(("message", e.Message));
                            into.Trace.Add(Value.Map(entries.ToArray()));
                            break;
                        }
                    case Effect.Jumped e:
                        into.Trace.Add(e.Node == null
                            ? Value.Map(("kind", "jump"), ("board", Board(e.Board)))
                            : Value.Map(("kind", "jump"), ("board", Board(e.Board)), ("node", e.Node)));
                        break;
                    case Effect.Ended e:
                        into.Trace.Add(e.RanOut
                            ? Value.Map(("kind", "ranOut"))
                            : e.Title == null ? Value.Map(("kind", "end")) : Value.Map(("kind", "end"), ("title", e.Title)));
                        break;
                    case Effect.Failed e:
                        into.Trace.Add(e.Node == null
                            ? Value.Map(("kind", "error"), ("message", e.Message))
                            : Value.Map(("kind", "error"), ("message", e.Message), ("node", e.Node)));
                        break;
                    case Effect.Hold e:
                        into.Trace.Add(Value.Map(("kind", "wait"), ("ms", (long)e.Ms)));
                        break;
                    case Effect.AmbienceChanged e:
                        into.Trace.Add(Value.Map(("kind", "ambience"), ("soundscape", e.Ambience?.Soundscape.Id)));
                        break;
                    case Effect.Audio e when e.Command.Kind == AudioKind.Cue:
                        {
                            var entries = new List<(string, Value)> { ("kind", "sound") };
                            if (e.Command.Node is { } node)
                            {
                                entries.Add(("node", node));
                            }
                            entries.Add(("media", campaign.MediaPath(e.Command.Media) ?? ""));
                            into.Trace.Add(Value.Map(entries.ToArray()));
                            break;
                        }
                    case Effect.Callback e:
                        {
                            var fired = e.Event;
                            var entries = new List<(string, Value)> { ("callback", fired.Callback), ("event", fired.EventName) };
                            if (fired.Board is { } board)
                            {
                                entries.Add(("board", Board(board)));
                            }
                            if (fired.Node is { } node)
                            {
                                entries.Add(("node", node));
                            }
                            if (fired.Name is { } named)
                            {
                                entries.Add(("name", named));
                            }
                            if (fired.From is { IsNull: false } from)
                            {
                                entries.Add(("from", from));
                            }
                            if (fired.To is { IsNull: false } to)
                            {
                                entries.Add(("to", to));
                            }
                            if (fired.Index is { } index)
                            {
                                entries.Add(("index", (long)index));
                            }
                            into.Callbacks.Add(Value.Map(entries.ToArray()));
                            break;
                        }
                }
            }

            private string Board(uint at) => at < boards.Count ? boards[(int)at] : "";

            // A field's name and type, `optional` only when it is, and the IDs
            // of its options only when it has some. Labels are not recorded.
            private static Value Field(PromptField field)
            {
                var entries = new List<(string, Value)> { ("name", field.Name), ("type", field.Type) };
                if (field.Optional)
                {
                    entries.Add(("optional", true));
                }
                if (field.Options.Count > 0)
                {
                    entries.Add(("options", Value.List(field.Options.Select(option => Value.Text(option.Id)))));
                }
                return Value.Map(entries.ToArray());
            }
        }

        // The same verbs whichever API is underneath, and where the story is.
        private interface IHost : IDisposable
        {
            Effect? Turn { get; }

            /// <summary>The session underneath, for what a host tells it rather than asks.</summary>
            Session Session { get; }

            void Start();

            void Choose(int index);

            void Leave();

            void Answer(IReadOnlyList<KeyValuePair<string, Value>>? values);

            void Elapsed();

            void BeginCountdown();

            void Expire();

            void Permit(PermissionKind kind, bool granted);

            void SetLocale(string locale);

            string? Pause();

            void Resume(string save);

            void Restart();

            IReadOnlyList<Entity> Entities();
        }

        // Through a Session: every batch settled, while it ends on a hold, by
        // saying `Elapsed` at once — a fixture's clock never waits.
        private sealed class SessionHost : IHost
        {
            private readonly Campaign campaign;
            private readonly ulong seed;
            private readonly Recorder record;
            private Session session;

            public SessionHost(Campaign campaign, Session session, ulong seed, Recorder record)
            {
                this.campaign = campaign;
                this.session = session;
                this.seed = seed;
                this.record = record;
            }

            public Effect? Turn { get; private set; }

            public Session Session => session;

            public void Start() => Settle(session.Start());

            public void Choose(int index) => Settle(session.Choose(index));

            public void Leave() => Settle(session.Leave());

            public void Answer(IReadOnlyList<KeyValuePair<string, Value>>? values) => Settle(session.Answer(values));

            public void Elapsed() => Settle(session.Elapsed());

            public void BeginCountdown() => Settle(session.BeginCountdown());

            public void Expire() => Settle(session.Expire());

            public void Permit(PermissionKind kind, bool granted) => Settle(session.Permit(kind, granted));

            public void SetLocale(string locale) => Settle(session.SetLocale(locale));

            public string? Pause()
            {
                var paused = session.Pause();
                Settle(paused.Effects);
                return paused.Save;
            }

            public void Resume(string save)
            {
                var generator = session.Generator;
                session.Dispose();
                session = Session.Resume(campaign, save, seed, FixtureOptions());
                session.Generator = generator;
            }

            public void Restart() => Settle(session.Restart());

            public IReadOnlyList<Entity> Entities() => session.Entities;

            public void Dispose() => session.Dispose();

            private void Settle(IReadOnlyList<Effect> batch)
            {
                while (true)
                {
                    foreach (var effect in batch)
                    {
                        record.Record(effect);
                        if (effect.IsTurn)
                        {
                            Turn = effect;
                        }
                    }
                    if (batch.Count == 0 || batch[batch.Count - 1] is not Effect.Hold)
                    {
                        return;
                    }
                    batch = session.Elapsed();
                }
            }
        }

        // Through a Player, on a clock that is a number: every hold it is
        // waiting on is run to its end by moving the clock and calling Update().
        private sealed class PlayerHost : IHost
        {
            private readonly Campaign campaign;
            private readonly ulong seed;
            private readonly Recorder record;
            private Player player;
            private ulong now;

            public PlayerHost(Campaign campaign, Session session, ulong seed, Recorder record)
            {
                this.campaign = campaign;
                this.seed = seed;
                this.record = record;
                player = Make(session);
            }

            public Effect? Turn => player.Turn;

            public Session Session => player.Session;

            public void Start() => Run(() => player.Start());

            public void Choose(int index) => Run(() => player.Choose(index));

            public void Leave() => Run(() => player.Leave());

            public void Answer(IReadOnlyList<KeyValuePair<string, Value>>? values) => Run(() => player.Answer(values));

            public void Elapsed()
            {
                foreach (var effect in player.Session.Elapsed())
                {
                    record.Record(effect);
                }
            }

            public void BeginCountdown() => Run(() => player.BeginCountdown());

            public void Expire() => Run(() => player.Expire());

            public void Permit(PermissionKind kind, bool granted) => Run(() => player.Permit(kind, granted));

            public void SetLocale(string locale) => Run(() => player.SetLocale(locale));

            public string? Pause()
            {
                var save = player.Pause();
                Flush();
                return save;
            }

            public void Resume(string save)
            {
                var generator = player.Session.Generator;
                player.Dispose();
                var session = Session.Resume(campaign, save, seed, FixtureOptions());
                session.Generator = generator;
                player = Make(session);
            }

            public void Restart() => Run(() => player.Restart());

            public IReadOnlyList<Entity> Entities() => player.Session.Entities;

            public void Dispose() => player.Dispose();

            private Player Make(Session session) => new Player(
                session,
                new PlayerHandlers
                {
                    OnEffect = record.Record,
                    OnError = error => Assert.Fail(error.ToString()),
                    // The moves run countdowns, through the player's own verbs.
                    Countdowns = false,
                },
                () => now);

            private void Run(Action verb)
            {
                verb();
                Flush();
            }

            private void Flush()
            {
                while (player.HoldDue is { } due)
                {
                    now = Math.Max(now, due);
                    player.Update();
                }
            }
        }
    }
}
