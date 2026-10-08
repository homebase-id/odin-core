#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// The Chat app, as provisioned, can add a reviewed contact to Friends and to Emergency Location Access.
/// </summary>
/// <remarks>
/// Chat owns neither -- Friends is the Contacts app's, Emergency Location Access the Location app's -- so
/// ownership alone would refuse it.  It gets in through <see cref="PermissionKeys.ManageCircleMembership"/>,
/// which stays in Chat's default registration for exactly this, and it can supply the drive keys both circles
/// need because its registration reads the ProfileDrive and the LocationDrive.  The session is the Chat
/// registration provisioning made, not a re-registration, so these pin the shipped defaults.
/// </remarks>
[TestFixture]
public class ChatAddsToBuiltinCirclesTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ChatsDefaultRegistrationHoldsManageCircleMembership()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var chat = (await frodo.RefitFor<IRefitOwnerAppRegistration>()
            .GetRegisteredApp(new GetAppRequest { AppId = SystemAppConstants.ChatAppId })).Content;

        Assert.That(chat, Is.Not.Null, "precondition: the Chat app should be provisioned");
        Assert.That(chat!.Grant.PermissionSet.Keys, Does.Contain(PermissionKeys.ManageCircleMembership));
    }

    [TestCase("Friends", TestName = "ChatAddsAReviewedContact_ToFriends")]
    [TestCase("Emergency Location Access", TestName = "ChatAddsAReviewedContact_ToEmergencyLocationAccess")]
    public async Task ChatAddsAReviewedContact(string circleName)
    {
        var circle = BuiltinApps.AllCircles.Single(c => c.Name == circleName);
        Assert.That(circle.AppId, Is.Not.EqualTo(SystemAppConstants.ChatAppId), "precondition: Chat must not own the circle");

        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        var review = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"arrange: review failed: {review.StatusCode}");

        var chat = await AppSession.ForRegisteredAppAsync(frodo, SystemAppConstants.ChatAppId);
        var add = await new V2ConnectionNetworkClient(chat.Identity, chat.Factory).GrantCircleAsync(circle.Id.Value, sam.Identity);

        Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Chat could not add to {circleName}: {add.StatusCode}");
        Assert.That(await IsInCircleAsync(frodo, sam.Identity, circle), Is.True,
            $"Sam should be in {circleName}, as a grant or a deposit awaiting the owner");
    }

    private async Task<bool> IsInCircleAsync(OwnerSession owner, OdinId member, CircleDefinition circle)
    {
        var icr = await Host.GetTenantScope(owner.Identity.DomainName).Resolve<CircleNetworkStorage>().GetAsync(member);
        Assert.That(icr, Is.Not.Null);
        return icr!.PeerKeyStore.CircleGrants.ContainsKey(circle.Id) ||
               icr.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circle.Id);
    }
}
