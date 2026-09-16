#nullable enable
using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/CircleGrantTests</c>. An auto-connection — one that
/// arrived through an introduction and that nobody confirmed — may not be granted a personal
/// circle; confirming it moves the contact out of Auto-connected into Confirmed Connections and
/// the same grant then succeeds.
/// </summary>
/// <remarks>
/// <para>
/// Passive polls are gone: <c>WaitForEmptyOutbox(TransientTempDrive)</c> becomes
/// <c>Sync.DrainOutboxAsync()</c>. The fast host registers the outbox background service but never
/// starts it, so a V1-style poll would hang out its timeout and then throw.
/// </para>
/// <para>
/// Carried defect, behaviour left exactly as found:
/// <see cref="CannotGrantCircleWhenIdentityInAutoAcceptCircle"/> is a strict prefix of
/// <see cref="CanGrantCircleAfterConfirmConnection"/> — every line of the first appears verbatim in
/// the second, up to and including the refused grant. The first therefore adds no coverage the
/// second does not already have. Both are carried as found.
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

        await PrepareAsync(frodo, sam, merry);

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
        Assert.That(samConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(samConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        // Try to grant before confirming connection
        var grantCircleResponse = await merry.Connections.GrantCircle(targetCircle, sam.Identity);
        Assert.That(grantCircleResponse.IsSuccessStatusCode, Is.False);
    }

    [Test]
    public async Task CanGrantCircleAfterConfirmConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await frodo.Admin.DisableAutoAcceptIntroductions();
        await sam.Admin.DisableAutoAcceptIntroductions();
        await merry.Admin.DisableAutoAcceptIntroductions();

        await PrepareAsync(frodo, sam, merry);

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
        Assert.That(samConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(samConnectionInfoResponse.Content.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        // Try to grant before confirming connection
        var grantCircleResponse = await merry.Connections.GrantCircle(targetCircle, sam.Identity);
        Assert.That(grantCircleResponse.IsSuccessStatusCode, Is.False);

        //merry confirms - now sam should be in confirmed circle
        var merryConfirmationResponse = await merry.Connections.ConfirmConnection(sam.Identity);
        Assert.That(merryConfirmationResponse.IsSuccessStatusCode, Is.True);

        var samConnectionInfoResponse2 = await merry.Connections.GetConnectionInfo(sam.Identity);
        Assert.That(samConnectionInfoResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(samConnectionInfoResponse2.Content!.AccessGrant.CircleGrants,
            Has.None.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.AutoConnectionsCircleId));
        Assert.That(samConnectionInfoResponse2.Content.AccessGrant.CircleGrants,
            Has.Some.Matches<RedactedCircleGrant>(cg => cg.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId));

        // try to add
        var grantCircleResponse2 = await merry.Connections.GrantCircle(targetCircle, sam.Identity);
        Assert.That(grantCircleResponse2.IsSuccessStatusCode, Is.True);
    }

    /// <summary>
    /// You have 3 hobbits. Frodo is connected to Sam and Merry; Sam and Merry are not connected.
    /// </summary>
    private static async Task PrepareAsync(OwnerSession frodo, OwnerSession sam, OwnerSession merry)
    {
        await frodo.Connections.SendConnectionRequest(sam.Identity, []);
        await frodo.Connections.SendConnectionRequest(merry.Identity, []);

        await merry.Connections.AcceptConnectionRequest(frodo.Identity);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);
    }

    private static IRefitUniversalCircleNetworkRequests Requests(OwnerSession owner) =>
        owner.RefitFor<IRefitUniversalCircleNetworkRequests>();
}
