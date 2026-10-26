// A passage's countdown on the player's clock, which no fixture runs: a
// fixture's clock never waits, so its moves say when a countdown begins and
// ends, and the conformance driver turns the player's own off. Here the
// player keeps the time on a clock that is a number.
//
// The fixture is `the-hesitation`: the bridge waits two seconds, then counts
// five, and takes `Wait` when it runs out, which leads to the gorge, whose
// countdown begins at once.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace RippleFx.Engine.Tests
{
    public class Countdowns
    {
        private static readonly string[] Started = { "countdown.started" };
        private static readonly string[] StartedThenExpired = { "countdown.started", "countdown.expired" };

        [Test]
        public void ThePlayerBeginsAndRunsOutACountdownOnItsClock()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-hesitation"));
            ulong now = 1000;
            var fired = new List<CallbackEvent>();
            using var player = new Player(
                Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0], Options = Conformance.FixtureOptions() }),
                new PlayerHandlers { OnError = error => Assert.Fail(error.ToString()) },
                () => now);
            player.OnAnyCallback(fired.Add);
            player.Start();

            var bridge = ((Effect.Show)player.Turn!).Passage;
            Assert.That(bridge.Timer, Is.EqualTo(new ChoiceTimer { AfterMs = 2000, TimeoutMs = 5000, Visible = true, Default = 1 }));
            Assert.That(player.CountdownBegins, Is.EqualTo(3000));
            Assert.That(player.CountdownExpires, Is.Null);

            now = 2999;
            player.Update();
            Assert.That(fired, Is.Empty, "the wait before a countdown is not over yet");

            now = 3400;
            player.Update();
            Assert.That(fired.Select(one => one.EventName), Is.EqualTo(Started));
            // Measured from when it was due, not from when Update came round.
            Assert.That(player.CountdownBegins, Is.Null);
            Assert.That(player.CountdownExpires, Is.EqualTo(8000));

            // Another language is the same visit: the countdown goes on.
            player.SetLocale("it-IT");
            Assert.That(((Effect.Show)player.Turn!).Passage.Text, Is.EqualTo("Il ponte di corda oscilla."));
            Assert.That(player.CountdownExpires, Is.EqualTo(8000));

            now = 8000;
            player.Update();
            Assert.That(fired.Select(one => one.EventName), Is.EqualTo(StartedThenExpired));
            Assert.That(fired[1].Index, Is.EqualTo(1), "the default it took");
            var gorge = ((Effect.Show)player.Turn!).Passage;
            Assert.That(gorge.Node, Is.EqualTo("narration_3"));
            // A new visit, whose countdown begins at once.
            Assert.That(player.CountdownBegins, Is.EqualTo(8000));

            // A choice drops the countdown it was waiting on: whatever is
            // scheduled now is the next passage's, from now.
            now = 9000;
            player.Choose(0);
            Assert.That(player.CountdownExpires, Is.Null);
            Assert.That(player.CountdownBegins, Is.Null.Or.GreaterThanOrEqualTo(9000));
        }

        [Test]
        public void ACountdownTheHostDoesNotRunIsNeverBegun()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-hesitation"));
            ulong now = 0;
            var fired = new List<CallbackEvent>();
            using var player = new Player(
                Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0], Options = Conformance.FixtureOptions() }),
                new PlayerHandlers { Countdowns = false, OnError = error => Assert.Fail(error.ToString()) },
                () => now);
            player.OnAnyCallback(fired.Add);
            player.Start();
            Assert.That(player.CountdownBegins, Is.Null);
            now = 60_000;
            player.Update();
            player.Update();
            Assert.That(fired, Is.Empty, "neither begun nor run out");
            Assert.That(player.CountdownExpires, Is.Null);
            Assert.That(((Effect.Show)player.Turn!).Passage.Node, Is.EqualTo("narration_2"));
        }

        // A verb that moves nothing leaves both waits owed: a permit once the
        // story has begun (host API §7a: at any time), a refused choice, a
        // language change on a hold. Each answers no batch, or throws, so
        // nothing would ever set the wait again.
        [Test]
        public void AVerbThatMovesNothingKeepsTheWaits()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-hesitation"));
            ulong now = 1000;
            using var player = new Player(
                Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] }),
                new PlayerHandlers { OnError = error => Assert.Fail(error.ToString()) },
                () => now);
            player.Start();
            Assert.That(player.CountdownBegins, Is.EqualTo(3000));
            player.Permit(PermissionKind.Location, false);
            Assert.That(player.CountdownBegins, Is.EqualTo(3000), "a permit mid-story");
            Assert.Throws<HostException>(() => player.Choose(99));
            Assert.That(player.CountdownBegins, Is.EqualTo(3000), "a refused choice");
            now = 3000;
            player.Update();
            Assert.That(player.CountdownExpires, Is.EqualTo(8000));
            player.Permit(PermissionKind.DateTime, true);
            Assert.That(player.CountdownExpires, Is.EqualTo(8000), "a permit while it runs");

            using var hourglass = Campaign.OpenDirectory(Fixtures.Bundle("the-hourglass"));
            now = 1000;
            using var fuse = new Player(
                Session.Create(hourglass, new SessionConfig { Board = hourglass.Manifest.Entries[0] }),
                new PlayerHandlers { OnError = error => Assert.Fail(error.ToString()) },
                () => now);
            fuse.Start();
            fuse.Choose(0);
            Assert.That(fuse.Turn, Is.EqualTo(new Effect.Hold(1500)));
            fuse.Permit(PermissionKind.Location, true);
            fuse.SetLocale("en-US");
            Assert.That(fuse.HoldDue, Is.EqualTo(2500));
            now = 2500;
            fuse.Update();
            Assert.That(((Effect.Show)fuse.Turn!).Passage.Node, Is.EqualTo("narration_4"));
        }

        [Test]
        public void TheEngineRefusesACountdownOutOfOrder()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-hesitation"));
            using var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
            session.Start();
            var early = Assert.Throws<HostException>(() => session.Expire())!;
            Assert.That(early.Code, Is.EqualTo(Code.CountdownNotBegun));
            Assert.That(session.BeginCountdown().OfType<Effect.Callback>().Single().Event.EventName, Is.EqualTo("countdown.started"));
            Assert.That(session.BeginCountdown(), Is.Empty, "a second begin moves nothing");

            using var choir = Campaign.OpenDirectory(Fixtures.Bundle("the-choir"));
            using var untimed = Session.Create(choir, new SessionConfig { Board = choir.Manifest.Entries[0] });
            var shown = (Effect.Show)Last(untimed.Start());
            Assert.That(shown.Passage.Timer, Is.Null);
            Assert.That(Assert.Throws<HostException>(() => untimed.BeginCountdown())!.Code, Is.EqualTo(Code.NoCountdown));
        }

        private static Effect Last(IReadOnlyList<Effect> batch) => batch[batch.Count - 1];
    }
}
