#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Contacts;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Profile;

namespace Odin.Hosting.Tests.V2.Ported.Circles;

/// <summary>
/// A change to who is in a circle applies to the member's very next call, not when their cached permission
/// context happens to expire.
/// </summary>
/// <remarks>
/// Peer permission contexts are cached by token for up to an hour.  Before the cache was reset on a single add or
/// revoke, a member removed from a circle kept reading what it shared for that long.
/// </remarks>
[TestFixture]
public class CircleMembershipTakesEffectImmediatelyTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ARemovedMember_LosesWhatTheCircleShared_OnTheirNextCall()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        var review = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"arrange: review failed: {review.StatusCode}");

        var circleId = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleId, "close friends", new PermissionSetGrantRequest
        {
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive { Drive = WellKnownAppDrives.ProfileDrive, Permission = DrivePermission.Read }
                }
            ]
        });
        var grant = await frodo.Connections.GrantCircle(circleId, sam.Identity);
        Assert.That(grant.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"arrange: grant failed: {grant.StatusCode}");

        var attribute = await new V2ProfileClient(frodo.Identity, frodo.Factory).SetAttributeAsync(new SetProfileAttributeRequest
        {
            Type = BuiltInProfileAttributes.ProfileCard,
            Priority = 0,
            Visibility = ProfileAttributeVisibility.Connected,
            CircleIds = [circleId],
            Data = new Dictionary<string, object> { ["design"] = "poster" }
        });
        Assert.That(attribute.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"arrange: setting the attribute failed: {attribute.StatusCode}");
        var uniqueId = attribute.Content!.Id;
        var fileType = (await new DriveReaderV2Client(frodo.Identity, frodo.Factory)
            .GetFileHeaderByUniqueIdAsync(uniqueId, WellKnownAppDrives.ProfileDrive.Alias)).Content!.FileMetadata.AppData.FileType;

        Assert.That(await SamSeesItAsync(sam, frodo, fileType, uniqueId), Is.True, "precondition: a member sees the card");

        var revoke = await frodo.Connections.RevokeCircle(circleId, sam.Identity);
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"revoke failed: {revoke.StatusCode}");

        Assert.That(await SamSeesItAsync(sam, frodo, fileType, uniqueId), Is.False,
            "removed from the circle, Sam must not see what it shares on his very next call");
    }

    private static async Task<bool> SamSeesItAsync(OwnerSession sam, OwnerSession frodo, int fileType, Guid uniqueId)
    {
        var response = await sam.RefitFor<IUniversalRefitPeerQuery>().GetBatch(new PeerQueryBatchRequest
        {
            OdinId = frodo.Identity,
            QueryParams = new FileQueryParamsV1 { TargetDrive = WellKnownAppDrives.ProfileDrive, FileType = [fileType] },
            ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
        });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"peer query failed: {response.StatusCode}");
        return response.Content!.SearchResults.Any(h => h.FileMetadata.AppData.UniqueId == uniqueId);
    }
}
