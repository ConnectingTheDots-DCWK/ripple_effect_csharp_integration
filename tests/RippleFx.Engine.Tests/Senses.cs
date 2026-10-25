// What a story reads of the world outside it, handed in by the host: the
// fixtures carry a stand-in and an instant across, and never a real fix, so
// a position's every field is carried here, through the World nodes of
// `the-weathervane`, which need no interpreter. And a form's words, which a
// fixture does not record.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace RippleFx.Engine.Tests
{
    public class Senses
    {
        private static readonly Moment Evening = Moment.Of(new DateTimeOffset(2026, 10, 19, 18, 30, 0, TimeSpan.FromHours(2)));

        private static readonly string[] BothKinds = { "dateTime", "location" };
        private static readonly string[] EnglishLabels = { "Alone", "A pair", "A band" };

        [Test]
        public void AStoryThatReadsTheWorldAsksBeforeItBegins()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-weathervane"));
            Assert.That(campaign.Manifest.Permissions, Is.EqualTo(BothKinds));
            using var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });

            var asked = session.Start();
            var permit = (Effect.Permit)Last(asked);
            Assert.That(permit.Kinds, Is.EqualTo(new[] { PermissionKind.DateTime, PermissionKind.Location }));
            Assert.That(asked.Where(effect => effect is Effect.Show or Effect.Callback or Effect.Jumped), Is.Empty,
                "nothing begins before the answers: no story.started, no page");

            Assert.That(session.Permit(PermissionKind.DateTime, true), Is.Empty, "a kind is still unanswered");
            var begun = session.Permit(PermissionKind.Location, false);
            Assert.That(begun[begun.Count - 1], Is.InstanceOf<Effect.Show>());

            var refused = Assert.Throws<HostException>(() => session.SetPosition(new Position { Latitude = 1, Longitude = 2 }))!;
            Assert.That(refused.Code, Is.EqualTo(Code.NotPermitted));
            Assert.That(refused.Failure.Subject, Is.EqualTo("location"));
        }

        [Test]
        public void AFixReachesTheStoryWhole()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-weathervane"));
            var effects = new List<Effect>();
            using var player = new Player(
                Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] }),
                new PlayerHandlers { OnEffect = effects.Add, Now = () => Evening });
            // A fix handed in before the answer is kept, and read once it is yes.
            player.Session.SetPosition(new Position
            {
                Label = "Paris",
                Latitude = 48.8566,
                Longitude = 2.3522,
                Accuracy = 12.5,
                When = Evening,
            });
            player.Permit(PermissionKind.DateTime, true);
            player.Permit(PermissionKind.Location, true);
            player.Start();

            Assert.That(((Effect.Show)player.Turn!).Passage.Text, Does.Contain("2026-10-19T18:30:00+02:00"), "the player's clock, at its offset");
            player.Choose(0);
            var shown = ((Effect.Show)player.Turn!).Passage.Text;
            Assert.That(shown, Does.Contain("Paris"));
            Assert.That(shown, Does.Contain("48.8566"));
            Assert.That(shown, Does.Contain("2.3522"));
            Assert.That(effects.OfType<Effect.Notice>(), Is.Empty, "every read got the real value");
        }

        [Test]
        public void AStandInIsTextTheEngineReads()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-weathervane"));
            using var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
            var wrong = Assert.Throws<HostException>(() => session.SetStandIn(PermissionKind.DateTime, "half past six", 0))!;
            Assert.That(wrong.Code, Is.EqualTo(Code.StandIn));
            Assert.That(wrong.Failure.Subject, Is.EqualTo("dateTime"));

            session.SetStandIn(PermissionKind.DateTime, "2026-10-19 18:30", 120);
            session.SetStandIn(PermissionKind.Location, "41.9028, 12.4964", 0);
            session.Permit(PermissionKind.DateTime, false);
            session.Permit(PermissionKind.Location, false);
            var batch = session.Start();
            Assert.That(((Effect.Show)Last(batch)).Passage.Text, Does.Contain("2026-10-19T18:30:00+02:00"), "read at the offset given");
            batch = session.Choose(0);
            var notice = batch.OfType<Effect.Notice>().Single();
            Assert.That(notice, Is.EqualTo(new Effect.Notice("I.RT.05", PermissionKind.Location, "location_get_5",
                "This story read the location and was given the stand-in “41.9028, 12.4964”.")));
        }

        [Test]
        public void AFormIsWordedInTheReadersLanguage()
        {
            using var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-chosen"));
            using var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
            var english = ((Effect.Ask)Last(session.Start())).Prompt;
            var italian = ((Effect.Ask)Last(session.SetLocale("it-IT"))).Prompt;

            var calling = english.Fields[0];
            Assert.That((calling.Name, calling.Label, calling.Optional, calling.Placeholder), Is.EqualTo(("calling", "Calling", false, "")));
            Assert.That(calling.Default, Is.EqualTo(Value.Text("wizard")));
            Assert.That(italian.Fields[0].Name, Is.EqualTo("calling"), "a name is what an answer is keyed by, in every language");
            Assert.That(italian.Fields[0].Label, Is.EqualTo("Vocazione"));

            var companions = italian.Fields[1];
            Assert.That(companions.Optional, Is.True);
            Assert.That(companions.Options, Is.EqualTo(new[]
            {
                new PromptOption("alone", "Da solo", Value.Int(0)),
                new PromptOption("pair", "Una coppia", Value.Int(2)),
                new PromptOption("band", "Una banda", Value.Int(5)),
            }));
            Assert.That(english.Fields[1].Options.Select(option => option.Label), Is.EqualTo(EnglishLabels));
        }

        private static Effect Last(IReadOnlyList<Effect> batch) => batch[batch.Count - 1];
    }
}
