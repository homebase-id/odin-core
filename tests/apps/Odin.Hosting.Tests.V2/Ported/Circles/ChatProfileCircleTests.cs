#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Circles;
using Odin.Services.Profile;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// The Chat app makes a profile circle of its own, the way Family, Friends and Work are made: it grants Read on
/// the ProfileDrive, which the Contacts app owns, and a reviewed contact Chat adds can then read a profile
/// attribute shared only with that circle.
/// </summary>
/// <remarks>
/// Chat does not own the ProfileDrive but its registration reads it, storage key included, so it may grant Read
/// there (<c>CircleDefinitionService.AssertAppMayGrantDrivesAsync</c>) and nothing more.  Chat's add is a
/// deposit -- it cannot reach the connection's Peer Key -- which converts when Sam's server next authenticates to
/// Frodo's, before Sam's permissions are built; the peer query is that call.  The session is the Chat
/// registration provisioning made, not a re-registration.
/// </remarks>
[TestFixture]
public class ChatProfileCircleTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly TargetDrive ProfileDrive = WellKnownAppDrives.ProfileDrive;

    [Test]
    public async Task ChatsProfileCircle_LetsAReviewedMemberReadAnAttributeSharedOnlyWithIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        var review = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"arrange: review failed: {review.StatusCode}");

        var chat = await AppSession.ForRegisteredAppAsync(frodo, SystemAppConstants.ChatAppId);
        var network = chat.RefitFor<IConnectionNetworkHttpClientApiV2>();

        var create = await network.CreateCircle(new CreateAppCircleRequest
        {
            Name = "Neighbours",
            Description = "People on my street",
            DriveGrants = [ReadOn(ProfileDrive)]
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"create failed: {create.StatusCode}");
        var neighbours = create.Content;

        var circle = await frodo.Admin.GetCircleDefinition(neighbours);
        Assert.That(circle.AppId, Is.EqualTo(SystemAppConstants.ChatAppId), "Chat owns the circle it made");
        Assert.That(circle.Designation, Is.EqualTo(CircleDesignation.Personal));
        Assert.That(circle.IsTreeDeclared, Is.False, "a user-made circle, not one of the built-in ones");
        Assert.That(circle.GrantOn, Is.EqualTo(CircleGrantOn.None));

        // A profile card only Neighbours may see.
        var attribute = await new V2ProfileClient(frodo.Identity, frodo.Factory).SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = BuiltInProfileAttributes.ProfileCard,
            Priority = 0,
            Visibility = ProfileAttributeVisibility.Connected,
            CircleIds = [neighbours],
            Data = new Dictionary<string, object> { ["design"] = "poster", ["label"] = "Neighbours" }
        });
        Assert.That(attribute.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"arrange: setting the attribute failed: {attribute.StatusCode}");
        var uniqueId = attribute.Content!.Id;
        var header = (await new DriveReaderV2Client(frodo.Identity, frodo.Factory)
            .GetFileHeaderByUniqueIdAsync(uniqueId, ProfileDrive.Alias)).Content!;
        var fileType = header.FileMetadata.AppData.FileType;

        Assert.That(await SamsViewAsync(sam, frodo, fileType, uniqueId), Is.Null, "not a member yet, so nothing to see");

        var add = await network.GrantCircle(new AddCircleMembershipRequest { CircleId = neighbours, OdinId = sam.Identity });
        Assert.That(add.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Chat could not add Sam: {add.StatusCode}");

        var seen = await SamsViewAsync(sam, frodo, fileType, uniqueId);
        Assert.That(seen, Is.Not.Null, "a member of Neighbours should see the card shared with it");
        Assert.That(seen!.SharedSecretEncryptedKeyHeader?.EncryptedAesKey, Is.Not.Null.And.Not.Empty,
            "and receive its key -- the card is encrypted, and Read without the key opens nothing");

        var delete = await network.DeleteCircle(neighbours, removeMembers: true);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"Chat could not delete the circle: {delete.StatusCode}");
        Assert.That(await SamsViewAsync(sam, frodo, fileType, uniqueId), Is.Null, "the circle is gone, and so is Sam's view");
    }

    /// <summary>Read is all an app may pass on from a drive it does not own; Write stays the owner's to grant.</summary>
    [Test]
    public async Task ChatCannotGrantWriteOnTheProfileDrive()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var chat = await AppSession.ForRegisteredAppAsync(frodo, SystemAppConstants.ChatAppId);

        var create = await chat.RefitFor<IConnectionNetworkHttpClientApiV2>().CreateCircle(new CreateAppCircleRequest
        {
            Name = "Editors",
            DriveGrants =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive { Drive = ProfileDrive, Permission = DrivePermission.Write }
                }
            ]
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static DriveGrantRequest ReadOn(TargetDrive drive) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } };

    /// <summary>The card as Sam's server sees it when it queries Frodo's ProfileDrive over peer, or null.</summary>
    private static async Task<Odin.Services.Apps.SharedSecretEncryptedFileHeader?> SamsViewAsync(OwnerSession sam,
        OwnerSession frodo, int fileType, Guid uniqueId)
    {
        var response = await sam.RefitFor<IUniversalRefitPeerQuery>().GetBatch(new PeerQueryBatchRequest
        {
            OdinId = frodo.Identity,
            QueryParams = new FileQueryParamsV1 { TargetDrive = ProfileDrive, FileType = [fileType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query failed: {response.StatusCode}");
        return response.Content!.SearchResults.SingleOrDefault(h => h.FileMetadata.AppData.UniqueId == uniqueId);
    }
}
