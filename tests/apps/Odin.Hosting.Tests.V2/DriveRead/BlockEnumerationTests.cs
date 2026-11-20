#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Hosting.UnifiedV2.Drive.Write;

namespace Odin.Hosting.Tests.V2.DriveRead;

/// <summary>
/// A drive that blocks enumeration (WebDrop's): anyone who may read it reaches a file by the unique id they were given,
/// as a capability link carries it, but only the owner, the owner's apps and the server may list it.  Each refusal is
/// pinned beside the same call on a drive that doesn't block it, which answers.
/// </summary>
[TestFixture]
public class BlockEnumerationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private const int FileType = 4300;

    private static readonly TargetDrive MeshDrive = TargetDrive.NewTargetDrive();

    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();
        await PeerFlow.ConnectAllAsync([await LoginAsOwner(Identities.Frodo), await LoginAsOwner(Identities.Sam)], MeshDrive);
    }

    [Test]
    public async Task OnlyTheOwnerListsADriveThatBlocksEnumeration()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var blocked = await CreateDriveAsync(frodo, blockEnumeration: true);
        var open = await CreateDriveAsync(frodo, blockEnumeration: false);
        var (blockedFile, _) = await UploadAsync(frodo, blocked);
        var (openFile, _) = await UploadAsync(frodo, open);

        // Anonymous
        Assert.That(await AnonymousQueryAsync(open), Is.EqualTo(HttpStatusCode.OK), "control: anyone lists a public drive");
        Assert.That(await AnonymousQueryAsync(blocked), Is.EqualTo(HttpStatusCode.Forbidden), "anonymous, on a drive that blocks it");

        // A connected identity, through its own server
        var openBySam = await PeerQueryAsync(sam, frodo, open);
        Assert.That((openBySam.StatusCode, openBySam.Content?.SearchResults.Any(h => h.FileId == openFile)),
            Is.EqualTo((HttpStatusCode.OK, (bool?)true)), "control: a connection lists a public drive");
        var blockedBySam = await PeerQueryAsync(sam, frodo, blocked);
        Assert.That((blockedBySam.StatusCode, blockedBySam.Content?.SearchResults.Any(h => h.FileId == blockedFile) ?? false),
            Is.EqualTo((HttpStatusCode.Forbidden, false)), "a connection, on a drive that blocks it");

        // The owner
        var byOwner = await frodo.RefitFor<IDriveQueryHttpClientApiV2>().GetBatch(blocked.Alias, Query(blocked));
        Assert.That((byOwner.StatusCode, byOwner.Content?.SearchResults.Any(h => h.FileId == blockedFile)),
            Is.EqualTo((HttpStatusCode.OK, (bool?)true)), "the owner lists it");
    }

    [Test]
    public async Task AnyoneWithTheLinkReadsAndExpiresAFileOnIt()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var blocked = await CreateDriveAsync(frodo, blockEnumeration: true);
        var (_, uid) = await UploadAsync(frodo, blocked, ttl: FileTtl.After(TimeSpan.FromHours(1)));
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();

        using var anonymous = Host.CreateAnonymousClient(Identities.Frodo);
        var byUid = $"/api/v2/drives/{blocked.Alias}/files/by-uid/{uid}";
        Assert.That((await anonymous.GetAsync($"{byUid}/header")).StatusCode, Is.EqualTo(HttpStatusCode.OK), "the header by its link");
        Assert.That((await anonymous.GetAsync($"{byUid}/payload/{payload.Key}")).StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the payload by its link");

        Assert.That((await anonymous.PostAsync($"{byUid}/expire-now", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK), "expire-now");
        Assert.That((await anonymous.GetAsync($"{byUid}/payload/{payload.Key}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "gone after expire-now");
    }

    [Test]
    public async Task ExpireNowIsRefusedOnADriveAnyoneCanList()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var open = await CreateDriveAsync(frodo, blockEnumeration: false);
        var (fileId, uid) = await UploadAsync(frodo, open, ttl: FileTtl.After(TimeSpan.FromHours(1)));

        using var anonymous = Host.CreateAnonymousClient(Identities.Frodo);
        var expire = await anonymous.PostAsync($"/api/v2/drives/{open.Alias}/files/by-uid/{uid}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "whoever lists it could expire every file on it");

        var header = await frodo.Drives.Reader.GetFileHeaderAsync(open.Alias, fileId);
        Assert.That(header.StatusCode, Is.EqualTo(HttpStatusCode.OK), header.Error?.Content);
        Assert.That(header.Content!.FileMetadata.Ttl, Is.GreaterThan(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            "the file is untouched: still alive");
    }

    [Test]
    public async Task TheOwnerTurnsItOnAndOffButNotOnASystemDrive()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(frodo, blockEnumeration: false);

        await frodo.Admin.SetBlockEnumeration(drive, true);
        Assert.That(await BlocksAsync(frodo, drive), Is.True, "on");
        Assert.That(await AnonymousQueryAsync(drive), Is.EqualTo(HttpStatusCode.Forbidden), "and enforced");

        await frodo.Admin.SetBlockEnumeration(drive, false);
        Assert.That(await BlocksAsync(frodo, drive), Is.False, "off");
        Assert.That(await AnonymousQueryAsync(drive), Is.EqualTo(HttpStatusCode.OK), "and listable again");

        var webDrop = await frodo.Admin.TrySetBlockEnumeration(WellKnownAppDrives.WebDropDrive, false);
        Assert.That(webDrop.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "a system drive keeps its built-in definition");
        Assert.That(await BlocksAsync(frodo, WellKnownAppDrives.WebDropDrive), Is.True, "WebDrop's drive, as provisioned");
    }

    [Test]
    public async Task TheOwnersAppListsIt()
    {
        // WebDrop's own list of drops (Waiting, Opened, Removed) is an app of the owner listing the drive
        var frodo = await LoginAsOwner(Identities.Frodo);
        var blocked = await CreateDriveAsync(frodo, blockEnumeration: true);
        var (fileId, _) = await UploadAsync(frodo, blocked);
        var app = await AppSession.SetupAsync(frodo, blocked, Odin.Services.Drives.DrivePermission.Read);

        var byApp = await app.RefitFor<IDriveQueryHttpClientApiV2>().GetBatch(blocked.Alias, Query(blocked));
        Assert.That((byApp.StatusCode, byApp.Content?.SearchResults.Any(h => h.FileId == fileId)),
            Is.EqualTo((HttpStatusCode.OK, (bool?)true)), byApp.Error?.Content);
    }

    [Test]
    public async Task ItCantBeCombinedWithSubscriptions()
    {
        // Followers would get every file pushed to them by the feed
        var frodo = await LoginAsOwner(Identities.Frodo);
        var create = await frodo.Admin.TryCreateDrive(TargetDrive.NewTargetDrive(), "both", allowAnonymousReads: true,
            allowSubscriptions: true, blockEnumeration: true);
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "at creation");

        var subscribed = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(subscribed, "subscribed", allowAnonymousReads: true, allowSubscriptions: true);
        var block = await frodo.Admin.TrySetBlockEnumeration(subscribed, true);
        Assert.That(block.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "on a drive that allows subscriptions");
    }

    [Test]
    public async Task TheOwnerExpiresAFileOnADriveAnyoneCanList()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var open = await CreateDriveAsync(frodo, blockEnumeration: false);
        var (_, uid) = await UploadAsync(frodo, open, ttl: FileTtl.After(TimeSpan.FromHours(1)));

        using var http = frodo.Factory.CreateHttpClient(frodo.Identity, out _);
        var expire = await http.PostAsync($"/api/v2/drives/{open.Alias}/files/by-uid/{uid}/expire-now", null);
        Assert.That(expire.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the owner may list and delete it anyway");
    }

    // Helpers

    private async Task<TargetDrive> CreateDriveAsync(OwnerSession owner, bool blockEnumeration)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, blockEnumeration ? "blocks enumeration" : "listable", allowAnonymousReads: true,
            blockEnumeration: blockEnumeration);
        return drive;
    }

    private static async Task<(Guid FileId, Guid Uid)> UploadAsync(OwnerSession owner, TargetDrive drive, long? ttl = null)
    {
        var uid = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: FileType, acl: AccessControlList.Anonymous);
        metadata.AppData.UniqueId = uid;
        if (ttl != null)
        {
            metadata.Ttl = ttl.Value;
        }

        List<TestPayloadDefinition> payloads = [SamplePayloadDefinitions.GetPayloadDefinition1()];
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        var response = await owner.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata, manifest, payloads);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        return (response.Content!.FileId, uid);
    }

    private static QueryBatchRequest Query(TargetDrive drive) => new()
    {
        QueryParams = new FileQueryParamsV1 { TargetDrive = drive, FileType = [FileType] },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
    };

    private async Task<HttpStatusCode> AnonymousQueryAsync(TargetDrive drive) =>
        (await Host.AnonymousRefitFor<IDriveQueryHttpClientApiV2>(Identities.Frodo).GetBatch(drive.Alias, Query(drive))).StatusCode;

    private static Task<Refit.ApiResponse<QueryBatchResponse>> PeerQueryAsync(OwnerSession member, OwnerSession sharer, TargetDrive drive) =>
        member.Drives.Peer.QueryBatchAsync(sharer.Identity, drive.Alias, Query(drive));

    private static async Task<bool> BlocksAsync(OwnerSession owner, TargetDrive drive) =>
        (await owner.Admin.GetDrives()).Single(d => d.TargetDriveInfo == drive).BlockEnumeration;
}
