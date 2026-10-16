// Every conformance fixture of the pinned engine release, played through this
// binding's own API — Session and Player — and compared with what the fixture
// says a reader saw, the way the release's FORMAT.md describes. The engine's
// own suites judge its rules; this judges the spelling: that every effect
// crosses into C# whole, an Int stays an Int and a Float a Float, and a host
// built on these types sees what any host sees.

using System;
using System.Collections.Generic;
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
            // Twenty-four fixtures, three of which need the interpreter.
            Assert.That(played + skipped, Is.EqualTo(24), "fixtures found");
            Assert.That(skipped, Is.EqualTo(python ? 0 : 3), "fixtures skipped for want of Python");
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

            string? saved = null;
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
                        break;
                    case "restart":
                        host.Restart();
                        break;
                }
            }
            // The reader has left: the story goes on by itself until it is over.
            while (!Over(host.Turn))
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
                            into.Trace.Add(Value.Map(entries.ToArray()));
                            break;
                        }
                    case Effect.Ask e:
                        into.Trace.Add(Value.Map(
                            ("kind", "prompt"),
                            ("node", e.Prompt.Node),
                            ("fields", Value.List(e.Prompt.Fields.Select(field => Value.Map(("name", field.Name), ("type", field.Type)))))));
                        break;
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
        }

        // The same verbs whichever API is underneath, and where the story is.
        private interface IHost : IDisposable
        {
            Effect? Turn { get; }

            void Start();

            void Choose(int index);

            void Leave();

            void Answer(IReadOnlyList<KeyValuePair<string, Value>>? values);

            void Elapsed();

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

            public void Start() => Settle(session.Start());

            public void Choose(int index) => Settle(session.Choose(index));

            public void Leave() => Settle(session.Leave());

            public void Answer(IReadOnlyList<KeyValuePair<string, Value>>? values) => Settle(session.Answer(values));

            public void Elapsed() => Settle(session.Elapsed());

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
                Settle(session.Start());
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
                Run(() => player.Start());
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
