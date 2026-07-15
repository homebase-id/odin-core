using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Tests.Membership.Connections;

[TestFixture]
public class IdentityConnectionRegistrationTests
{
    [Test]
    public void Redacted_ConfirmedConnection_IsVetted()
    {
        var icr = CreateIcr(SystemCircleConstants.ConfirmedConnectionsCircleId);

        Assert.That(icr.Redacted().Vetted, Is.True);
    }

    [Test]
    public void Redacted_AutoConnectedOnly_IsNotVetted()
    {
        var icr = CreateIcr(SystemCircleConstants.AutoConnectionsCircleId);

        Assert.That(icr.Redacted().Vetted, Is.False);
    }

    [Test]
    public void Redacted_ConfirmedButNotConnected_IsNotVetted()
    {
        var icr = CreateIcr(SystemCircleConstants.ConfirmedConnectionsCircleId);
        icr.Status = ConnectionStatus.Blocked;

        Assert.That(icr.Redacted().Vetted, Is.False);
    }

    private static IdentityConnectionRegistration CreateIcr(Guid memberCircleId)
    {
        var icr = new IdentityConnectionRegistration
        {
            OdinId = new OdinId("frodo.dotyou.cloud"),
            AccessGrant = new AccessExchangeGrant
            {
                CircleGrants = new Dictionary<Guid, CircleGrant>
                {
                    [memberCircleId] = new CircleGrant
                    {
                        CircleId = memberCircleId,
                        KeyStoreKeyEncryptedDriveGrants = new()
                    }
                }
            }
        };
        icr.Status = ConnectionStatus.Connected;
        return icr;
    }
}
