using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests._Universal.ApiClient.Connections;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/Transit/Detection/TransitBadCATDetectionTests.cs
///
/// Merry disconnects from Pippin without telling him, so Pippin's next transit call still carries the
/// now-stale client access token. Merry's server rejects it and reports the bad CAT back; Pippin's
/// server drops the connection and falls back to anonymous access — so the public file still reads
/// and the secured one answers 403.
/// </summary>
/// <remarks>
/// Framework gap worked around: <c>_scaffold.Scenarios.CreateConnectedHobbits</c> has no V2
/// counterpart. It is reproduced as <see cref="HobbitScenario.ConnectAllAsync"/>, local to this
/// folder — see that class for what it does and does not carry over. It should probably be promoted
/// to shared framework once a second fixture needs it.
/// <para>
/// Other port notes: the transit payload read goes through the V1
/// <see cref="IRefitOwnerTransitQuery"/> via <c>owner.RefitFor</c> (there is no <c>_Universal</c>
/// client on the V1 handles for it, and its routes are already absolute). The disconnect likewise goes
/// through <see cref="IRefitUniversalCircleNetworkConnections"/> rather than
/// <c>owner.Connections.DisconnectFrom</c>, which does not expose <c>notifyRemote</c> — and
/// <c>notifyRemote: false</c> is the whole point of the scenario. The V1 client's own two assertions
/// on disconnect (call succeeded; the caller's connection status is then <c>None</c>) are carried
/// inline. File uploads use <c>UploadNewFile</c> with a one-payload manifest, the <c>_Universal</c>
/// spelling of the original's <c>UploadFile(..., payloadData:, payloadKey:)</c>.
/// </para>
/// <para>
/// Not a defect, but worth knowing: the original bound
/// <c>_scaffold.Scenarios.CreateConnectedHobbits(targetDrive)</c>'s return value to a local it never
/// read, and the hobbits' shared drive plays no part in the assertions — only the Merry/Pippin
/// connection the scenario creates does. The scenario is kept whole rather than reduced to that one
/// connection, since reducing it would change the arrange.
/// </para>
/// </remarks>
[TestFixture]
public class TransitBadCATDetectionTests : V2Fixture
{
    protected override string[] HostIdentities =>
        [Identities.Pippin, Identities.Merry, Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanDetectBadCAT_and_UpdateICR_and_FallbackToPublicAccess()
    {
        // Prepare Scenario

        // 1. Connect the hobbits
        var targetDrive = TargetDrive.NewTargetDrive();

        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await HobbitScenario.ConnectAllAsync([frodo, merry, pippin, sam], targetDrive);

        // 2. Merry posts two pieces of content 1 public, one secured that requires you to be connected
        //

        var (publicFileUploadResult, publicPayloadContent) = await MerryPostPublicFileAsync(merry);
        var (securedFileUploadResult, securedPayloadContent) = await MerryPostSecureFileAndAuthorizePippinAsync(merry, pippin.Identity);

        //
        // 3. Pippin is connected so he can read both via transit query service
        //
        var getPublicPayloadTransitResponse1 = await GetPayloadOverTransitAsync(pippin, merry.Identity, publicFileUploadResult.File);
        Assert.That(getPublicPayloadTransitResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getPublicPayloadTransitResponse1.Content, Is.Not.Null);

        var remotePublicPayload1 = await getPublicPayloadTransitResponse1.Content!.ReadAsStringAsync();
        Assert.That(remotePublicPayload1, Is.EqualTo(publicPayloadContent));

        var getSecuredPayloadTransitResponse1 = await GetPayloadOverTransitAsync(pippin, merry.Identity, securedFileUploadResult.File);
        Assert.That(getSecuredPayloadTransitResponse1.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getSecuredPayloadTransitResponse1.Content, Is.Not.Null);

        var remoteSecuredPayload1 = await getSecuredPayloadTransitResponse1.Content!.ReadAsStringAsync();
        Assert.That(remoteSecuredPayload1, Is.EqualTo(securedPayloadContent));

        //
        // Merry gets mad and disconnects from Pippin, pippin leaves for Gondor with Gandalf 🧙‍🐎
        // notifyRemote:false -- this scenario specifically requires Pippin to still *think* he's
        // connected so his next call carries a now-stale CAT, exercising bad-CAT detection below.
        //
        var disconnect = await merry.RefitFor<IRefitUniversalCircleNetworkConnections>()
            .Disconnect(new OdinIdRequest { OdinId = pippin.Identity }, notifyRemote: false);
        Assert.That(disconnect.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var pippinConnectionOnMerry = await merry.Connections.GetConnectionInfo(pippin.Identity);
        Assert.That(pippinConnectionOnMerry.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(pippinConnectionOnMerry.Content!.Status, Is.EqualTo(ConnectionStatus.None));

        //
        // Pippin makes transit query call to Merry still thinking they are connected (therefore he sends CAT).
        //
        // On the backend - Merry's server detects bad CAT (or ICR), rejects the call and Tells Pippin's server that the CAT is invalid
        // therefore, the call should fail with 403
        // Pippin's server sees bad CAT then and updates Merry's ICR with a flag indicating not to use the CAT

        var getPublicPayloadTransitResponse2 = await GetPayloadOverTransitAsync(pippin, merry.Identity, publicFileUploadResult.File);

        Assert.That(getPublicPayloadTransitResponse2.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getPublicPayloadTransitResponse2.Content, Is.Not.Null);

        var remotePublicPayload2 = await getPublicPayloadTransitResponse2.Content!.ReadAsStringAsync();
        Assert.That(remotePublicPayload2, Is.EqualTo(publicPayloadContent));

        // Call to secure should fail with 403
        var getSecurePayloadTransitResponse2 = await GetPayloadOverTransitAsync(pippin, merry.Identity, securedFileUploadResult.File);
        Assert.That(getSecurePayloadTransitResponse2.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        //
        // Validate there is no longer a connection with merry/pippin
        //
        var merryConnectionOnPippin = await pippin.Connections.GetConnectionInfo(merry.Identity);
        Assert.That(merryConnectionOnPippin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(merryConnectionOnPippin.Content!.Status, Is.EqualTo(ConnectionStatus.None));
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<Refit.ApiResponse<System.Net.Http.HttpContent>> GetPayloadOverTransitAsync(
        OwnerSession caller, OdinId remoteIdentity, ExternalFileIdentifier file)
    {
        var svc = caller.RefitFor<IRefitOwnerTransitQuery>();
        return await svc.GetPayload(new TransitGetPayloadRequest
        {
            OdinId = remoteIdentity,
            File = file,
            Key = WebScaffold.PAYLOAD_KEY
        });
    }

    private static async Task<(UploadResult UploadResult, string SecuredPayloadContent)>
        MerryPostSecureFileAndAuthorizePippinAsync(OwnerSession merry, OdinId pippin)
    {
        var merrySecuredDrive = TargetDrive.NewTargetDrive();
        await merry.Admin.CreateDrive(merrySecuredDrive, "a private blog",
            allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: true);

        var canAccessSecureDriveCircleId = System.Guid.NewGuid();
        await merry.Admin.CreateCircle(canAccessSecureDriveCircleId, "CanAccessSecureDrive", new PermissionSetGrantRequest
        {
            PermissionSet = new Odin.Services.Authorization.Permissions.PermissionSet(),
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = merrySecuredDrive,
                        Permission = DrivePermission.Read
                    }
                }
            }
        });

        var grant = await merry.Connections.GrantCircle(canAccessSecureDriveCircleId, pippin);
        Assert.That(grant.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        const string headerContent = "some secured header content";
        const string payloadContent = "this is the secured payload";

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            AppData = new UploadAppFileMetaData
            {
                FileType = 10101,
                Content = headerContent
            },
            IsEncrypted = false,
            AccessControlList = AccessControlList.Connected
        };

        var uploadResult = await UploadWithPayloadAsync(merry, merrySecuredDrive, fileMetadata, payloadContent);
        return (uploadResult, payloadContent);
    }

    private static async Task<(UploadResult UploadResult, string PublicPayloadContent)> MerryPostPublicFileAsync(OwnerSession merry)
    {
        var merryPublicDrive = TargetDrive.NewTargetDrive();
        await merry.Admin.CreateDrive(merryPublicDrive, "a public blog",
            allowAnonymousReads: true, ownerOnly: false, allowSubscriptions: true);

        const string headerContent = "some public header content";
        const string payloadContent = "this is the public payload";

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            AppData = new UploadAppFileMetaData
            {
                FileType = 10101,
                Content = headerContent
            },
            IsEncrypted = false,
            AccessControlList = AccessControlList.Anonymous
        };

        var uploadResult = await UploadWithPayloadAsync(merry, merryPublicDrive, fileMetadata, payloadContent);
        return (uploadResult, payloadContent);
    }

    /// <summary>One unencrypted file carrying a single plaintext payload under <c>WebScaffold.PAYLOAD_KEY</c>.</summary>
    private static async Task<UploadResult> UploadWithPayloadAsync(
        OwnerSession owner, TargetDrive targetDrive, UploadFileMetadata fileMetadata, string payloadContent)
    {
        var payload = new TestPayloadDefinition
        {
            Iv = null,
            Key = WebScaffold.PAYLOAD_KEY,
            ContentType = "text/plain",
            Content = payloadContent.ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = []
        };

        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var response = await owner.V1.Drive.UploadNewFile(targetDrive, fileMetadata, manifest, payloads);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content;
    }
}
