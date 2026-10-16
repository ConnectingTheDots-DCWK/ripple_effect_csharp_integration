// What a handle's Dispose means: a disposed campaign or session refuses a
// call rather than handing the engine a pointer it has freed, and a session
// outlives the campaign it was made from.

using System;
using NUnit.Framework;

namespace RippleFx.Engine.Tests
{
    public class Lifetime
    {
        [Test]
        public void ADisposedHandleRefusesACall()
        {
            var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-choir"));
            var session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
            session.Dispose();
            Assert.Throws<ObjectDisposedException>(() => session.Start());
            campaign.Dispose();
            Assert.Throws<ObjectDisposedException>(() => _ = campaign.Manifest);
        }

        [Test]
        public void ASessionOutlivesItsCampaign()
        {
            Session session;
            using (var campaign = Campaign.OpenDirectory(Fixtures.Bundle("the-choir")))
            {
                session = Session.Create(campaign, new SessionConfig { Board = campaign.Manifest.Entries[0] });
            }
            using (session)
            {
                var batch = session.Start();
                Assert.That(batch[batch.Count - 1], Is.InstanceOf<Effect.Show>());
                batch = session.Choose(0);
                Assert.That(batch[batch.Count - 1].IsTurn, Is.True);
            }
        }
    }
}
