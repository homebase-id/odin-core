#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Connections;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// An app (no master key) accepting an incoming connection request mints a "keyless" PeerKeyStore:
/// <c>MasterKeyEncryptedPeerKey</c> is null and the Peer Key survives only as the ECC-encrypted
/// <c>TempWeakKeyStoreKey</c> (<c>CircleNetworkRequestService.AcceptConnectionRequestAsync</c>).
/// <c>CircleNetworkService.GrantCircleAsync</c>'s owner branch must run the deferred master-key
/// upgrade before decrypting the Peer Key — without it the owner's next circles/add threw an NRE.
/// </summary>
[TestFixture]
public class AppAcceptedConnectionGrantTests : V2Fixture
{
    /// <remarks>
    /// Surfaced only once #1775 made attribution correct: accepting a connection runs a best-effort
    /// channel sync whose failure is swallowed and logged (<c>CircleNetworkRequestService</c>, the
    /// same catch-all shape as the two sites in #1770; now tracked on its own as #1784). Until that is fixed the error is not
    /// something this fixture can avoid provoking.
    /// </remarks>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        ["Failed while trying to sync channels"];

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task OwnerCanGrantCircle_AfterAppAcceptedTheConnectionRequest()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(appDrive, "appDrive", allowAnonymousReads: false);

        // ManageContacts passes the accept endpoint's gate; UseTransitWrite is what puts the ICR key
        // in the app's permission context — required to decrypt the incoming pending request (it's
        // ECC-encrypted under the OnlineIcrEncryptedKey).
        var app = await AppSession.SetupAsync(frodo, appDrive, DrivePermission.Read,
            permissionKeys: new[]
            {
                PermissionKeys.ManageContacts,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ManageCircleMembership,
                PermissionKeys.UseTransitWrite
            });

        // Sam asks to connect; the app on Frodo's side — not the owner — accepts.
        var sendReq = await new UniversalCircleNetworkRequestsApiClient(sam.Identity, sam.Factory)
            .SendConnectionRequest(frodo.Identity);
        Assert.That(sendReq.IsSuccessStatusCode, Is.True, $"SendConnectionRequest failed: {sendReq.StatusCode}");

        // No circles granted at accept time — the point of this test is the owner's later grant.
        var accept = await new V2ConnectionRequestsClient(app.Identity, app.Factory)
            .AcceptIncomingRequestAsync(sam.Identity, new AcceptConnectionRequestV2());
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"app accept failed: {accept.StatusCode} {accept.Error?.Content}");

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var before = await storage.GetAsync(sam.Identity);
        Assert.That(before, Is.Not.Null, "frodo should hold an ICR for sam after the app accepted");
        Assert.That(before!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.True,
            "precondition: an app-accepted connection has no master-key-encrypted peer key");

        var circleA = Guid.NewGuid();
        var created = await frodo.Admin.CreateCircle(circleA, "circleA", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = appDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        });
        Assert.That(created.IsSuccessStatusCode, Is.True, $"CreateCircle failed: {created.StatusCode}");

        // The owner grant: previously a 500 (NullReferenceException on MasterKeyEncryptedPeerKey).
        var grant = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory)
            .GrantCircleAsync(circleA, sam.Identity);
        Assert.That(grant.IsSuccessStatusCode, Is.True, $"owner GrantCircle failed: {grant.StatusCode}");

        var after = await storage.GetAsync(sam.Identity);
        Assert.That(after!.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade(), Is.False,
            "the grant should have upgraded the peer key to master-key encryption");
        Assert.That(after.PeerKeyStore.CircleGrants.ContainsKey(circleA), Is.True,
            "sam should hold a real CircleGrant for circleA (owner branch, not a deposit)");
        Assert.That(after.PeerKeyStore.DepositedGrants.Any(d => d.CircleId == circleA), Is.False,
            "the owner path grants directly; nothing should be left pending");

        var members = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).GetCircleMembersAsync(circleA);
        Assert.That(members.IsSuccessStatusCode, Is.True, $"GetCircleMembers failed: {members.StatusCode}");
        Assert.That(members.Content!.Any(m => m == sam.Identity), Is.True, "sam should appear as a member of circleA");
    }

    [Test]
    public async Task AppAcceptingWithReadCircle_MintsGrantWithStorageKey_AndSamCanDecrypt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var appDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(appDrive, "appDrive", allowAnonymousReads: false);

        // Shaped like Emergency Location Access: an app-owned circle granting Read on the app's own drive.
        var appId = Guid.NewGuid();
        var readCircle = Guid.NewGuid();
        var created = await frodo.Admin.CreateCircle(readCircle, "read-circle", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new() { PermissionedDrive = new PermissionedDrive { Drive = appDrive, Permission = DrivePermission.Read } }
            },
            PermissionSet = new PermissionSet(new List<int>())
        }, appId: appId);
        Assert.That(created.IsSuccessStatusCode, Is.True, $"CreateCircle failed: {created.StatusCode}");

        // The app can read appDrive itself, so it is able to source the storage key it hands out.
        var app = await AppSession.SetupAsync(frodo, appDrive, DrivePermission.Read,
            permissionKeys: new[]
            {
                PermissionKeys.ManageContacts,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ManageCircleMembership,
                PermissionKeys.UseTransitWrite
            },
            knownAppId: appId);

        // Encrypted before the connection exists, so reading it back depends on the grant's storage key.
        const string plaintext = "only readable with a keyed read grant";
        var metadata = SampleMetadataData.Create(fileType: 7031, acl: AccessControlList.Connected);
        metadata.AppData.Content = plaintext;
        var (upload, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(
            appDrive.Alias, metadata, new TransitOptions(), keyHeader: KeyHeader.NewRandom16());
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"encrypted upload failed: {upload.StatusCode}");
        var fileId = upload.Content!.FileId;

        var sendReq = await new UniversalCircleNetworkRequestsApiClient(sam.Identity, sam.Factory)
            .SendConnectionRequest(frodo.Identity);
        Assert.That(sendReq.IsSuccessStatusCode, Is.True, $"SendConnectionRequest failed: {sendReq.StatusCode}");

        var accept = await new V2ConnectionRequestsClient(app.Identity, app.Factory)
            .AcceptIncomingRequestAsync(sam.Identity, new AcceptConnectionRequestV2 { CircleIds = [readCircle] });
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"app accept failed: {accept.StatusCode} {accept.Error?.Content}");
        await frodo.Sync.DrainOutboxAsync();

        var storage = Host.GetTenantScope(frodo.Identity.DomainName).Resolve<CircleNetworkStorage>();
        var icr = await storage.GetAsync(sam.Identity);
        Assert.That(icr!.PeerKeyStore.CircleGrants.TryGetValue(readCircle, out var circleGrant), Is.True,
            "the app's accept should have put sam in the read circle");
        var driveGrant = circleGrant!.KeyStoreKeyEncryptedDriveGrants.Single(dg => dg.PermissionedDrive.Drive == appDrive);
        Assert.That(driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.Read), Is.True);
        Assert.That(driveGrant.KeyStoreKeyEncryptedStorageKey, Is.Not.Null,
            "a read grant minted by an app that can read the drive must carry the storage key, not be keyless");

        // Strongest proof: Sam reads the encrypted file over peer and decrypts it with Sam's own shared secret.
        var headerResp = await sam.Drives.Peer.GetFileHeaderAsync(frodo.Identity, appDrive.Alias, fileId);
        Assert.That(headerResp.IsSuccessStatusCode, Is.True, $"peer header read failed: {headerResp.StatusCode}");
        var header = headerResp.Content!;
        Assert.That(header.FileMetadata.IsEncrypted, Is.True);

        var samSecret = sam.Drives.Reader.GetSharedSecret();
        var keyHeader = header.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref samSecret);
        var decrypted = keyHeader.Decrypt(header.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes();
        Assert.That(decrypted, Is.EqualTo(plaintext));
    }
}
