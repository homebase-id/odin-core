using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Tests.Membership.Connections;

/// <summary>
/// The redacted shape's <c>Vetted</c> flag, which is now a compatibility alias for
/// <c>ReviewedAt != null</c> rather than a lookup of Confirmed-circle membership.
/// </summary>
[TestFixture]
public class IdentityConnectionRegistrationTests
{
    [Test]
    public void Redacted_ReviewedConnection_IsVetted()
    {
        var icr = CreateIcr(reviewedAt: UnixTimeUtc.Now());

        var redacted = icr.Redacted();

        Assert.That(redacted.Vetted, Is.True);
        Assert.That(redacted.ReviewedAt, Is.Not.Null, "new clients read reviewedAt; vetted is the old name for it");
    }

    [Test]
    public void Redacted_UnreviewedConnection_IsNotVetted()
    {
        var icr = CreateIcr(reviewedAt: null);

        var redacted = icr.Redacted();

        Assert.That(redacted.Vetted, Is.False);
        Assert.That(redacted.ReviewedAt, Is.Null);
    }

    [Test]
    public void Redacted_ReviewedThenBlocked_IsStillVetted()
    {
        // Vetted follows the review and nothing else. Blocking someone does not un-review them --
        // the owner did once look at this contact and vouch for them -- and Status is what says they
        // are blocked now. The old flag folded connectedness in, so this answer changed with it.
        var icr = CreateIcr(reviewedAt: UnixTimeUtc.Now());
        icr.Status = ConnectionStatus.Blocked;

        var redacted = icr.Redacted();

        Assert.That(redacted.Vetted, Is.True);
        Assert.That(redacted.Status, Is.EqualTo(ConnectionStatus.Blocked));
    }

    private static IdentityConnectionRegistration CreateIcr(UnixTimeUtc? reviewedAt)
    {
        var icr = new IdentityConnectionRegistration
        {
            OdinId = new OdinId("frodo.dotyou.cloud"),
            PeerKeyStore = new PeerKeyStore(),
            ReviewedAt = reviewedAt
        };

        icr.Status = ConnectionStatus.Connected;
        return icr;
    }
}
