#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Util;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Concepts;

/// <summary>
/// The caller matrix the two <c>_Universal/Concepts</c> fixtures share. Deliberately <b>not</b>
/// <see cref="CallerSpec"/>: both of their non-owner rows grant permission <i>keys only</i>, with no
/// drive grant at all, which <see cref="CallerSpec.App"/> / <see cref="CallerSpec.Guest"/> cannot
/// express — they always attach a <c>DriveGrantRequest</c>. Building one here would either create a
/// drive the original never created or grant access the original never granted.
/// </summary>
/// <remarks>
/// Each row is named for the V1 <c>IApiClientContext</c> it came from so a failure names the same
/// thing the original did. The guest row reproduces
/// <c>ConnectedIdentityLoggedInOnGuestApi(TestIdentities.Pippin.OdinId, …)</c>: a YouAuth domain
/// named for the acting identity itself. In both originals the identity that context was built
/// against <i>is</i> Pippin, so deriving the domain from the owner session keeps it identical.
/// </remarks>
public sealed record CollabCallerSpec(string Name, Func<OwnerSession, Task<IV2Caller>> Build)
{
    public override string ToString() => Name;

    /// <summary>V1 <c>OwnerClientContext</c>.</summary>
    public static CollabCallerSpec Owner() =>
        new("OwnerClientContext", o => Task.FromResult<IV2Caller>(o));

    /// <summary>V1 <c>AppPermissionsKeysOnly(TestPermissionKeyList(PermissionKeys.UseTransitWrite))</c>.</summary>
    public static CollabCallerSpec AppWithOnlyUseTransitWrite() =>
        new("AppPermissionsKeysOnly", async o => await AppSession.SetupAsync(o, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitWrite)
        }));

    /// <summary>
    /// V1 <c>ConnectedIdentityLoggedInOnGuestApi(Pippin, TestPermissionKeyList(PermissionKeys.ReadWhoIFollow))</c>.
    /// Unlike <c>GuestSpecifyAccessToDrive</c> (whose keys are silently dropped — see the README), this
    /// context really does put its keys on the circle, so they are carried.
    /// </summary>
    public static CollabCallerSpec ConnectedIdentityLoggedInOnGuestApi() =>
        new("ConnectedIdentityLoggedInOnGuestApi", async o => await GuestSession.SetupAsync(o,
            new PermissionSetGrantRequest
            {
                PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
            },
            new AsciiDomainName(o.Identity.DomainName)));
}

/// <summary>
/// What both <c>_Universal/Concepts</c> ports share: the arrange — a collaboration-channel drive on
/// one identity, a circle granting members access to it, and the connection handshake that puts them
/// in that circle — and the encrypted post they send over it, either straight to the channel's own
/// drive or over peer-direct.
/// </summary>
/// <remarks>
/// The <c>IsCollaborativeChannel</c> attribute is load-bearing rather than cosmetic:
/// <c>PeerFileUpdateWriter.DetermineAclAsync</c> keeps the sender's ACL on the collaboration
/// channel's copy instead of narrowing it to owner-only, which is the whole point of both fixtures.
/// <see cref="Peer.PeerFlow.ConnectAllAsync"/> is not used — it builds a mesh with no drive
/// attributes and a circle per identity, whereas these scenarios need a star (every member connects
/// to the collaboration channel only) over one attributed drive the channel alone hosts.
/// </remarks>
public static class CollabScenario
{
    public static async Task DisableAutoAcceptIntroductionsAsync(params OwnerSession[] sessions)
    {
        foreach (var session in sessions)
        {
            await session.Admin.DisableAutoAcceptIntroductions();
        }
    }

    /// <summary>
    /// Creates the collaboration-channel drive on <paramref name="collabChannel"/> and the circle
    /// members are granted on connect, then runs the request/accept handshake for each member.
    /// Returns the circle id, which the fixtures also use as a file ACL.
    /// </summary>
    /// <param name="circleId">
    /// Fixed circle id, for a fixture that bakes this scenario into its baseline snapshot and so
    /// cannot read a freshly-minted one back out. Omit for a new one.
    /// </param>
    public static async Task<Guid> PrepareScenarioAsync(
        OwnerSession collabChannel,
        IReadOnlyList<OwnerSession> members,
        TargetDrive collabChannelDrive,
        DrivePermission memberPermission,
        string driveName,
        bool allowAnonymousReads,
        Guid? circleId = null)
    {
        await DisableAutoAcceptIntroductionsAsync([collabChannel, .. members]);

        var createDriveResponse = await collabChannel.Admin.CreateDrive(collabChannelDrive, driveName,
            allowAnonymousReads: allowAnonymousReads,
            allowSubscriptions: true, //required for distributing push notifications
            attributes: DriveSpec.CollabAttributes);
        Assert.That(createDriveResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var theCircleId = circleId ?? Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, memberPermission);
        await collabChannel.Admin.CreateCircle(theCircleId, "circle with some access", permissions);

        foreach (var member in members)
        {
            var send = await member.Connections.SendConnectionRequest(collabChannel.Identity);
            Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var accept = await collabChannel.Connections.AcceptConnectionRequest(
                member.Identity, [(GuidId)theCircleId]);
            Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        return theCircleId;
    }

    /// <summary>
    /// The encrypted post both fixtures send: file type 100, data type 7779, distribution allowed,
    /// under <paramref name="acl"/>, carrying the two thumbnailed sample payloads each under its own
    /// fresh IV, plus the manifest describing them.
    /// </summary>
    /// <remarks>
    /// Payload IVs have to be set explicitly. <c>SamplePayloadDefinitions</c> hands back definitions
    /// with no IV, and an encrypted upload needs one per payload — which is why both originals did
    /// this by hand, three times over, before it moved here.
    /// </remarks>
    private static (UploadFileMetadata Metadata, UploadManifest Manifest, List<TestPayloadDefinition> Payloads)
        NewEncryptedPost(AccessControlList acl)
    {
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 7779;
        uploadedFileMetadata.AccessControlList = acl;

        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payload1.Iv = ByteArrayUtil.GetRndByteArray(16);
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();
        payload2.Iv = ByteArrayUtil.GetRndByteArray(16);

        var payloads = new List<TestPayloadDefinition> { payload1, payload2 };
        var manifest = new UploadManifest
        {
            PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList()
        };

        return (uploadedFileMetadata, manifest, payloads);
    }

    /// <summary>
    /// Sends <see cref="NewEncryptedPost"/> to <paramref name="collabChannel"/> over peer-direct and
    /// drains the sender's outbox. Returns the transfer response, the metadata as sent, and the first
    /// payload — the one the update tests go on to delete.
    /// </summary>
    public static async Task<(ApiResponse<TransitResult> Response, UploadFileMetadata Metadata, TestPayloadDefinition Payload1)>
        PostNewEncryptedFileOverPeerDirectAsync(
            OwnerSession sender,
            TargetDrive collabChannelDrive,
            OwnerSession collabChannel,
            KeyHeader keyHeader,
            AccessControlList acl,
            AppNotificationOptions? notificationOptions = null)
    {
        var (metadata, manifest, payloads) = NewEncryptedPost(acl);

        var (response, _) = await sender.V1.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            metadata, [collabChannel.Identity], null, manifest, payloads, notificationOptions,
            keyHeader: keyHeader);

        await sender.Sync.DrainOutboxAsync();

        return (response, metadata, payloads[0]);
    }

    /// <summary>
    /// Uploads <see cref="NewEncryptedPost"/> straight to <paramref name="sender"/>'s own drive — the
    /// same post, no peer hop — and drains the outbox the push notifications ride.
    /// </summary>
    public static async Task<ApiResponse<UploadResult>> UploadNewEncryptedFileAsync(
        OwnerSession sender,
        TargetDrive targetDrive,
        AccessControlList acl,
        AppNotificationOptions notificationOptions)
    {
        var (metadata, manifest, payloads) = NewEncryptedPost(acl);

        var (response, _, _, _) = await sender.V1.Drive.UploadNewEncryptedFile(
            targetDrive,
            KeyHeader.NewRandom16(),
            metadata,
            manifest,
            payloads,
            notificationOptions);

        await sender.Sync.DrainOutboxAsync();

        return response;
    }
}
