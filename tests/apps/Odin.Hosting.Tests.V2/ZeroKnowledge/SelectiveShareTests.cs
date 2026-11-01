#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Circles;
using Odin.Hosting.UnifiedV2.Drive.Shares;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Apps;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Shares;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Refit;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Shares on a Selective Share drive (docs/selective-share-pr3-plan.md, sections 2 and 3): who may write them, what each
/// entry must hold, what a share does to the file, and the read rule they feed: Sam, in the app's circle, reads one of
/// Frodo's files only through a live share, and Pippin, connected but in no circle of the app, never.
/// </summary>
[TestFixture]
public class SelectiveShareTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Pippin];

    private static readonly TargetDrive MeshDrive = new()
    {
        Alias = Guid.Parse("5e1ec7a1-0000-4000-8000-000000000001"),
        Type = Guid.Parse("5e1ec7a1-0000-4000-8000-000000000002")
    };

    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        var everyone = new List<OwnerSession>();
        foreach (var identity in HostIdentities)
        {
            everyone.Add(await LoginAsOwner(identity));
        }

        await PeerFlow.ConnectAllAsync(everyone, MeshDrive);
    }

    [Test]
    public async Task AShareLiftsAnOwnerFileToConnectedKeepingItsVersionTag()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner);
        var before = await s.HeaderAsync(fileId);

        await s.ShareAsync(fileId, DrivePermission.Read | DrivePermission.Write, expiresAt: UnixTimeUtc.Now().AddSeconds(3600));

        var after = await s.HeaderAsync(fileId);
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected), "lifted");
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag), "a share doesn't change the file");
        Assert.That(after.FileMetadata.Updated.milliseconds, Is.GreaterThan(before.FileMetadata.Updated.milliseconds), "but moves its modified, for sync");

        var share = (await s.SharesAsync(fileId)).Single();
        Assert.That((share.CircleId, share.Permissions), Is.EqualTo((s.CircleId, DrivePermission.Read | DrivePermission.Write)));
        Assert.That(share.ExpiresAt, Is.Not.Null);

        // A second write changes the row, keeps its created and moves its modified
        await s.ShareAsync(fileId, DrivePermission.Read);
        var rewritten = (await s.SharesAsync(fileId)).Single();
        Assert.That((rewritten.Permissions, rewritten.ExpiresAt, rewritten.Created), Is.EqualTo((DrivePermission.Read, (UnixTimeUtc?)null, share.Created)));
        Assert.That(rewritten.Modified.milliseconds, Is.GreaterThan(share.Modified.milliseconds));

        // A header write leaves the share alone (section 1)
        var update = await s.Frodo.Drives.Writer.UpdateFileByFileId(s.Drive.Alias, fileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local, TransferIv = ByteArrayUtil.GetRndByteArray(16), Manifest = new UploadManifest { PayloadDescriptors = [] }
        },
            s.Metadata(SecurityGroupType.Connected, after.FileMetadata.VersionTag), []);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        Assert.That((await s.SharesAsync(fileId)).Single().Permissions, Is.EqualTo(DrivePermission.Read), "the share after an update");
    }

    [Test]
    public async Task AMemberReadsAFileOnlyThroughALiveShare()
    {
        var s = await ArrangeAsync();
        var pippin = await LoginAsOwner(Identities.Pippin);
        var shared = await s.UploadAsync(SecurityGroupType.Owner);
        var connectedButNotShared = await s.UploadAsync(SecurityGroupType.Connected);

        await AssertSamSeesAsync(s, shared, false, "before any share");
        await s.ShareAsync(shared, DrivePermission.Read);
        await AssertSamSeesAsync(s, shared, true, "shared with his circle");
        await AssertSamSeesAsync(s, connectedButNotShared, false, "a Connected file with no share: the owner's alone");
        await AssertSeesAsync(pippin, s, shared, false, "Pippin, in no circle of the app", hasDriveAccess: false);

        // An unreviewed connection counts as Authenticated, below the file's Connected
        var tenant = Host.GetTenantScope(s.Frodo.Identity.DomainName);
        await tenant.Resolve<CircleNetworkStorage>().UpdateReviewedAtAsync(s.Sam.Identity, ConnectionStatus.Connected, null);
        await tenant.Resolve<OdinContextCache>().ResetAsync();
        await AssertSamSeesAsync(s, shared, false, "unreviewed");
        var review = await new V2ConnectionNetworkClient(s.Frodo.Identity, s.Frodo.Factory).MarkReviewedAsync(s.Sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");
        await AssertSamSeesAsync(s, shared, true, "reviewed again");

        Assert.That((await s.Shares.WithdrawShare(s.Drive.Alias, shared, s.CircleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertSamSeesAsync(s, shared, false, "withdrawn");
        var withdrawn = (await s.SharesAsync(shared)).Single();
        Assert.That(withdrawn.Permissions, Is.EqualTo((DrivePermission)0), "a withdrawn share keeps its row");
        Assert.That((await s.HeaderAsync(shared)).ServerMetadata.AccessControlList.RequiredSecurityGroup,
            Is.EqualTo(SecurityGroupType.Connected), "withdrawing leaves the group");
    }

    [Test]
    public async Task AnExpiredShareLetsNoOneIn()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner);
        var expiresAt = UnixTimeUtc.Now().AddSeconds(3);
        await s.ShareAsync(fileId, DrivePermission.Read, expiresAt);
        await AssertSamSeesAsync(s, fileId, true, "before it expires");

        await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, expiresAt.milliseconds - UnixTimeUtc.Now().milliseconds) + 200));
        await AssertSamSeesAsync(s, fileId, false, "once it expired");
    }

    [Test]
    public async Task OnlyTheDrivesAppOrTheOwnerConsoleSharesWithTheAppsOwnCircle()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner);
        var request = s.Request(fileId, DrivePermission.Read);

        var otherApp = await AppSession.SetupAsync(s.Frodo, s.Drive, DrivePermission.All);
        Assert.That((await otherApp.RefitFor<IDriveSharesHttpClient>().WriteShares(s.Drive.Alias, request)).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden), "another app");

        var ownerCircle = Guid.NewGuid();
        await s.Frodo.Admin.CreateCircle(ownerCircle, "the owner's", s.ReadWriteGrant());
        Assert.That((await s.Frodo.RefitFor<IDriveSharesHttpClient>().WriteShares(s.Drive.Alias, s.Request(fileId, DrivePermission.Read, ownerCircle))).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden), "an owner circle");

        var keyless = Guid.NewGuid();
        await s.Frodo.Admin.CreateCircle(keyless, "no key yet", s.ReadWriteGrant(), s.App.AppId);
        DriveAsserts.AssertRejected(await s.Shares.WriteShares(s.Drive.Alias, s.Request(fileId, DrivePermission.Read, keyless)),
            OdinClientErrorCode.CircleShareKeyNotSet);

        var readOnly = await s.CircleWithKeyAsync(DrivePermission.Read);
        DriveAsserts.AssertRejected(await s.Shares.WriteShares(s.Drive.Alias, s.Request(fileId, DrivePermission.Read, readOnly)),
            OdinClientErrorCode.ArgumentError);

        Assert.That((await s.Frodo.RefitFor<IDriveSharesHttpClient>().WriteShares(s.Drive.Alias, request)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "the owner console");
        Assert.That((await s.Shares.WriteShares(s.Drive.Alias, request)).StatusCode, Is.EqualTo(HttpStatusCode.OK), "the drive's app");

        var collaborative = TargetDrive.NewTargetDrive();
        await s.Frodo.Admin.CreateDrive(collaborative, "not shared this way", allowAnonymousReads: false);
        DriveAsserts.AssertRejected(await s.Frodo.RefitFor<IDriveSharesHttpClient>().WriteShares(collaborative.Alias, request),
            OdinClientErrorCode.ArgumentError);
    }

    [Test]
    public async Task EachShareIsCheckedAndABatchIsAllOrNone()
    {
        var s = await ArrangeAsync();
        var plain = await s.UploadAsync(SecurityGroupType.Owner);
        var encrypted = await s.UploadEncryptedAsync();
        var later = UnixTimeUtc.Now().AddSeconds(3600);

        foreach (var (why, share, expected) in new (string, ShareRequest, OdinClientErrorCode)[]
                 {
                     ("no Read", s.Share(plain, DrivePermission.Write), OdinClientErrorCode.ArgumentError),
                     ("no bits", s.Share(plain, 0), OdinClientErrorCode.ArgumentError),
                     ("a bit a share can't hold", s.Share(plain, DrivePermission.Read | DrivePermission.ConditionalTemporalRead),
                         OdinClientErrorCode.ArgumentError),
                     ("expired", s.Share(plain, DrivePermission.Read, UnixTimeUtc.Now().AddSeconds(-1)), OdinClientErrorCode.ArgumentError),
                     ("a key on an unencrypted file", s.Share(plain, DrivePermission.Read, later, LockedFileKey()), OdinClientErrorCode.InvalidShareKey),
                     ("no key on an encrypted file", s.Share(encrypted, DrivePermission.Read), OdinClientErrorCode.InvalidShareKey),
                     ("a key of the wrong length", s.Share(encrypted, DrivePermission.Read, null, LockedFileKey(44)), OdinClientErrorCode.InvalidShareKey),
                     ("a key of another format", s.Share(encrypted, DrivePermission.Read, null, LockedFileKey(format: 1)), OdinClientErrorCode.InvalidShareKey),
                     ("no such file", s.Share(Guid.NewGuid(), DrivePermission.Read), OdinClientErrorCode.InvalidFile),
                 })
        {
            // Each refused alongside a good share of another file, which isn't stored either
            var response = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
            {
                Shares = [s.Share(share.FileId == plain ? encrypted : plain, DrivePermission.Read, null,
                    share.FileId == plain ? LockedFileKey() : null), share]
            });
            DriveAsserts.AssertRejected(response, expected);
            Assert.That(await s.SharesAsync(plain), Is.Empty, $"{why}: nothing stored");
            Assert.That(await s.SharesAsync(encrypted), Is.Empty, $"{why}: nothing stored");
        }

        DriveAsserts.AssertRejected(await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = Enumerable.Range(0, DriveShareService.MaxSharesPerRequest + 1).Select(_ => s.Share(Guid.NewGuid(), DrivePermission.Read)).ToList()
        }), OdinClientErrorCode.ArgumentError);
        DriveAsserts.AssertRejected(await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(plain, DrivePermission.Read), s.Share(plain, DrivePermission.Read)]
        }), OdinClientErrorCode.ArgumentError);

        var good = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(plain, DrivePermission.Read | DrivePermission.React | DrivePermission.Comment, later),
                s.Share(encrypted, DrivePermission.Read, null, LockedFileKey())]
        });
        Assert.That(good.StatusCode, Is.EqualTo(HttpStatusCode.OK), good.Error?.Content);
        Assert.That((await s.SharesAsync(encrypted)).Single().Permissions, Is.EqualTo(DrivePermission.Read));
    }

    [Test]
    public async Task ANewFilesUploadCarriesItsShares()
    {
        var s = await ArrangeAsync();

        // Sent as Owner, stored Connected, as a share lifts the file; the share is written with the header
        var plain = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write)]);
        Assert.That((await s.HeaderAsync(plain)).ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected));
        Assert.That((await s.SharesAsync(plain)).Single().Permissions, Is.EqualTo(DrivePermission.Read | DrivePermission.Write));
        await AssertSamSeesAsync(s, plain, true, "shared in its upload");

        var encrypted = await s.UploadEncryptedAsync([s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockedFileKey())]);
        Assert.That((await s.SharesAsync(encrypted)).Single().CircleId, Is.EqualTo(s.CircleId));

        // Each refused before anything is stored
        foreach (var (why, share) in new (string, ShareRequest)[]
                 {
                     ("a share without Read", s.Share(Guid.Empty, DrivePermission.Write)),
                     ("a share naming a file", s.Share(Guid.NewGuid(), DrivePermission.Read)),
                     ("a key on an unencrypted file", s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockedFileKey())),
                 })
        {
            var uniqueId = Guid.NewGuid();
            var refused = await s.TryUploadAsync(s.Frodo, SecurityGroupType.Owner, [share], uniqueId);
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{why}: {refused.Error?.Content}");
            var query = await s.Frodo.Drives.Reader.GetBatchAsync(s.Drive.Alias, QueryFor(uniqueId));
            Assert.That(query.Content!.SearchResults, Is.Empty, $"{why}: no file stored");
        }

        var otherApp = await AppSession.SetupAsync(s.Frodo, s.Drive, DrivePermission.All);
        var byOtherApp = await s.TryUploadAsync(otherApp, SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)], Guid.NewGuid());
        Assert.That(byOtherApp.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"another app's upload: {byOtherApp.Error?.Content}");
    }

    [Test]
    public async Task OwnerCallersGetAFilesLiveSharesOnlyWhenTheyAsk()
    {
        var s = await ArrangeAsync();
        var shared = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        var withdrawn = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        var notShared = await s.UploadAsync(SecurityGroupType.Owner);
        Assert.That((await s.Shares.WithdrawShare(s.Drive.Alias, withdrawn, s.CircleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        foreach (var (caller, who) in new (IV2Caller, string)[] { (s.Frodo, "the owner console"), (s.App, "the drive's app") })
        {
            var asked = await SharesInQueryAsync(caller, s, includeShares: true);
            Assert.That(asked[shared]?.Select(r => (r.CircleId, r.Permissions)), Is.EqualTo(new[] { (s.CircleId, DrivePermission.Read) }), who);
            Assert.That(asked[withdrawn], Is.Empty, $"{who}: a withdrawn share isn't live");
            Assert.That(asked[notShared], Is.Empty, $"{who}: no share");
            Assert.That((await SharesInQueryAsync(caller, s, includeShares: false)).Values, Has.All.Null, $"{who}: not asked");

            var byId = await caller.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, shared, includeShares: true);
            Assert.That(byId.Content?.Shares?.Single().CircleId, Is.EqualTo(s.CircleId), $"{who}, by id: {byId.Error?.Content}");
            Assert.That((await caller.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, shared)).Content?.Shares, Is.Null, $"{who}, by id, not asked");
        }
    }

    // Each file of the drive and the shares its header came with
    private static async Task<Dictionary<Guid, List<ShareRow>?>> SharesInQueryAsync(IV2Caller caller, Arranged s, bool includeShares)
    {
        var query = await caller.Drives.Reader.GetBatchAsync(s.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true, IncludeShares = includeShares }
        });
        Assert.That(query.IsSuccessStatusCode, Is.True, $"the query answered {query.StatusCode}: {query.Error?.Content}");
        return query.Content!.SearchResults.ToDictionary(h => h.FileId, h => (List<ShareRow>?)h.Shares);
    }

    private static async Task AssertSamSeesAsync(Arranged s, Guid fileId, bool sees, string when) =>
        await AssertSeesAsync(s.Sam, s, fileId, sees, when);

    // The read by id and the query answer alike: the read rule decides both
    private static async Task AssertSeesAsync(OwnerSession reader, Arranged s, Guid fileId, bool sees, string when,
        bool hasDriveAccess = true)
    {
        var byId = await reader.Drives.Peer.GetFileHeaderAsync(s.Frodo.Identity, s.Drive.Alias, fileId);
        Assert.That(byId.IsSuccessStatusCode, Is.EqualTo(sees), $"{when}: read by id answered {byId.StatusCode}: {byId.Error?.Content}");

        var query = await reader.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        });
        Assert.That(query.IsSuccessStatusCode, Is.EqualTo(hasDriveAccess), $"{when}: the query answered {query.StatusCode}: {query.Error?.Content}");
        Assert.That(query.Content?.SearchResults.Any(h => h.FileId == fileId) == true, Is.EqualTo(sees), $"{when}: in the query");
    }

    /// <summary>A stand-in for a file key locked with the circle's key: format 2, 45 bytes, which the server can't open.</summary>
    private static byte[] LockedFileKey(int length = DriveShareService.LockedFileKeyLength, byte format = CircleShareKeyService.FormatVersion)
    {
        var key = ByteArrayUtil.GetRndByteArray(length);
        key[0] = format;
        return key;
    }

    /// <summary>
    /// Frodo, zero knowledge, with an app owning a Selective Share drive and a circle on it (Read and Write) that holds its
    /// key and has Sam in it.
    /// </summary>
    private async Task<Arranged> ArrangeAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);

        var appId = Guid.NewGuid();
        await frodo.Admin.EnsureAppRegistered(appId);
        var drive = TargetDrive.NewTargetDrive();
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);
        await frodo.Admin.CreateDrive(drive, "Shared", allowAnonymousReads: false, appId: appId,
            sharingModel: DriveSharingModel.SelectiveShare, zkKeys: keys.Request);
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.All, [], knownAppId: appId);

        var s = new Arranged(frodo, sam, app, drive, keys.DriveKey);
        s.CircleId = await s.CircleWithKeyAsync(DrivePermission.Read | DrivePermission.Write);
        await AppCircles.AddMemberAsync(app, s.CircleId, Identities.Sam);
        return s;
    }

    private sealed class Arranged(OwnerSession frodo, OwnerSession sam, AppSession app, TargetDrive drive, byte[] driveKey)
    {
        public OwnerSession Frodo { get; } = frodo;
        public OwnerSession Sam { get; } = sam;
        public AppSession App { get; } = app;
        public TargetDrive Drive { get; } = drive;
        public Guid CircleId { get; set; }
        public IDriveSharesHttpClient Shares { get; } = app.RefitFor<IDriveSharesHttpClient>();

        public PermissionSetGrantRequest ReadWriteGrant(DrivePermission permission = DrivePermission.Read | DrivePermission.Write) => new()
        {
            Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = Drive, Permission = permission } }]
        };

        /// <summary>A circle of the app granting <paramref name="permission"/> on the drive, its key set.</summary>
        public async Task<Guid> CircleWithKeyAsync(DrivePermission permission)
        {
            var circleId = Guid.NewGuid();
            await Frodo.Admin.CreateCircle(circleId, "the app's circle", ReadWriteGrant(permission), App.AppId);
            var key = new byte[61];
            key[0] = CircleShareKeyService.FormatVersion;
            var set = await App.RefitFor<ICircleShareKeyHttpClient>().SetShareKey(circleId, new CircleShareKeyData { AppKeyLockedShareKey = key });
            Assert.That(set.StatusCode, Is.EqualTo(HttpStatusCode.OK), set.Error?.Content);
            return circleId;
        }

        public UploadFileMetadata Metadata(SecurityGroupType group, Guid? versionTag = null)
        {
            var metadata = SampleMetadataData.CreateWithContent(fileType: 100, "shared", new AccessControlList { RequiredSecurityGroup = group });
            metadata.VersionTag = versionTag;
            return metadata;
        }

        /// <summary>An unencrypted file, which a member's read can show before client-locked files reach members (section 6).</summary>
        public async Task<Guid> UploadAsync(SecurityGroupType group, List<ShareRequest>? shares = null)
        {
            var upload = await TryUploadAsync(Frodo, group, shares, Guid.NewGuid());
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
            return upload.Content!.FileId;
        }

        public Task<ApiResponse<CreateFileResult>> TryUploadAsync(IV2Caller caller, SecurityGroupType group, List<ShareRequest>? shares,
            Guid uniqueId)
        {
            var metadata = Metadata(group);
            metadata.AppData.UniqueId = uniqueId;
            return caller.Drives.Writer.CreateNewUnencryptedFile(Drive.Alias, metadata, new UploadManifest { PayloadDescriptors = [] }, [],
                shares: shares);
        }

        public async Task<Guid> UploadEncryptedAsync(List<ShareRequest>? shares = null)
        {
            var fileKey = KeyHeader.NewRandom16();
            var (upload, _, _, _) = await Frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(SecurityGroupType.Owner),
                transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, driveKey), shares: shares);
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
            return upload.Content!.FileId;
        }

        public ShareRequest Share(Guid fileId, DrivePermission permissions, UnixTimeUtc? expiresAt = null, byte[]? lockedFileKey = null,
            Guid? circleId = null) =>
            new() { FileId = fileId, CircleId = circleId ?? CircleId, Permissions = permissions, ExpiresAt = expiresAt, LockedFileKey = lockedFileKey };

        public WriteSharesRequest Request(Guid fileId, DrivePermission permissions, Guid? circleId = null) =>
            new() { Shares = [Share(fileId, permissions, circleId: circleId)] };

        public async Task ShareAsync(Guid fileId, DrivePermission permissions, UnixTimeUtc? expiresAt = null)
        {
            var response = await Shares.WriteShares(Drive.Alias, new WriteSharesRequest { Shares = [Share(fileId, permissions, expiresAt)] });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
        }

        public async Task<List<ShareRow>> SharesAsync(Guid fileId)
        {
            var response = await Shares.GetShares(Drive.Alias, fileId);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
            return response.Content!;
        }

        public async Task<SharedSecretEncryptedFileHeader> HeaderAsync(Guid fileId)
        {
            var response = await Frodo.Drives.Reader.GetFileHeaderAsync(Drive.Alias, fileId);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
            return response.Content!;
        }
    }
}

public interface IDriveSharesHttpClient
{
    // The share routes without their :guid route constraints, which Refit refuses
    private const string Root = UnifiedApiRouteConstants.DrivesRoot + "/{driveId}";

    [Get(Root + "/files/{fileId}/shares")]
    Task<ApiResponse<List<ShareRow>>> GetShares(Guid driveId, Guid fileId);

    [Put(Root + "/shares")]
    Task<ApiResponse<HttpContent>> WriteShares(Guid driveId, [Body] WriteSharesRequest request);

    [Delete(Root + "/files/{fileId}/shares/{circleId}")]
    Task<ApiResponse<HttpContent>> WithdrawShare(Guid driveId, Guid fileId, Guid circleId);
}
