#nullable enable
using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/CircleGrantTests</c>. An auto-connection — one that
/// arrived through an introduction and that nobody confirmed — may not be granted a personal
/// circle.  (The original also confirmed the connection and granted again; the confirm-connection
/// endpoint is gone, so that half is too.)
/// </summary>
/// <remarks>
/// <para>
/// Passive polls are gone: <c>WaitForEmptyOutbox(TransientTempDrive)</c> becomes
/// <c>Sync.DrainOutboxAsync()</c>. The fast host registers the outbox background service but never
/// starts it, so a V1-style poll would hang out its timeout and then throw.
/// </para>
/// <para>
/// The trailing <c>Cleanup()</c> was lifecycle only (disconnect every pairing) and is dropped —
/// <c>V2Fixture</c> restores the DB before each test. The circle-create arrange moved onto
/// <c>owner.Admin.CreateCircle</c>, which sends the same request the original's
/// <c>Network.CreateCircle</c> did (same id / name / grant, same generated description); nothing
/// here reads the description. <c>SetupCallerWithOwner</c> is not in play — no caller matrix,
/// <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class CircleGrantTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    [Test]
    public async Task CannotGrantCircleWhenIdentityInAutoAcceptCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await frodo.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();
        await merry.Admin.DisableAutoAcceptIntroductions();

        await PrepareIntroducer(frodo, sam, merry);

        var targetCircle = Guid.NewGuid();
        var merryCreatesCircleResponse = await merry.Admin.CreateCircle(targetCircle, "some circle",
            new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(PermissionKeys.AllowIntroductions)
            });
        Assert.That(merryCreatesCircleResponse.IsSuccessStatusCode, Is.True);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        await frodo.Sync.DrainOutboxAsync();

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        //ensure sam sends a request
        var samProcessResponse = await Requests(sam).ProcessIncomingIntroductions();
        Assert.That(samProcessResponse.IsSuccessStatusCode, Is.True);

        var merryProcessResponse = await Requests(merry).ProcessIncomingIntroductions();
        Assert.That(merryProcessResponse.IsSuccessStatusCode, Is.True);

        await Requests(sam).AutoAcceptEligibleIntroductions();
        await Requests(merry).AutoAcceptEligibleIntroductions();

        //validate they are connected
        var samConnectionInfoResponse = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(samConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        Assert.That(samConnectionInfoResponse.Content!.Status, Is.EqualTo(ConnectionStatus.Connected));
        Assert.That(samConnectionInfoResponse.Content.ReviewedAt, Is.Null, "an auto-accepted introduction is not reviewed");

        // Try to grant before confirming connection
        var grantCircleResponse = await merry.Connections.GrantCircle(targetCircle, sam.Identity);
        Assert.That(grantCircleResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
