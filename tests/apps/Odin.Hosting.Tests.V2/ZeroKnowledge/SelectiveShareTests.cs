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
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
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
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Apps;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Shares;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming.Reactions;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Reactions;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Core.Storage;
using Refit;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Shares on a Selective Share drive (docs/selective-share-pr3-plan.md, sections 2 and 3): who may write them, what each
/// entry must hold, what a share does to the file, and the read rule they feed: Sam, in the app's circle, reads one of
/// Frodo's files only through a live share, and Pippin, connected but in no circle of the app, never.
/// </summary>
[TestFixture]
public partial class SelectiveShareTests : V2Fixture
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
        await s.OwnerEditAsync(fileId, SecurityGroupType.Connected);
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
        await UnreviewSamAsync(s);
        await AssertSamSeesAsync(s, shared, false, "unreviewed");
        await ReviewSamAsync(s);
        await AssertSamSeesAsync(s, shared, true, "reviewed again");

        Assert.That((await s.Shares.WithdrawShare(s.Drive.Alias, shared, s.CircleId)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await AssertSamSeesAsync(s, shared, false, "withdrawn");
        await AssertSamGetsTheTombstoneAsync(s, shared, "withdrawn");
        var withdrawn = (await s.SharesAsync(shared)).Single();
        Assert.That(withdrawn.Permissions, Is.EqualTo((DrivePermission)0), "a withdrawn share keeps its row");
        Assert.That((await s.HeaderAsync(shared)).ServerMetadata.AccessControlList.RequiredSecurityGroup,
            Is.EqualTo(SecurityGroupType.Connected), "withdrawing leaves the group");
    }

    [Test]
    public async Task AMemberBelowTheFilesGroupGetsNothingThroughALiveShare()
    {
        // An unreviewed connection counts as Authenticated, below a shared file's Connected: their live share lets them in
        // to nothing, the file's comments included, and they may change nothing; their sync gets the file's tombstone,
        // without the share or its key
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write)]);
        var file = await s.GtidOfAsync(fileId);
        await OwnerCommentAsync(s, file, null, null);
        await AssertSamSeesAsync(s, fileId, true, "reviewed");

        await UnreviewSamAsync(s);
        try
        {
            await AssertSamSeesAsync(s, fileId, false, "unreviewed");
            await AssertSamGetsTheTombstoneAsync(s, fileId, "unreviewed");
            Assert.That((await SamsCommentsAsync(s, file)).Select(c => c.FileState), Is.EqualTo(new[] { FileState.Deleted }),
                "its comments go with it, as tombstones");

            // His socket hears of Frodo's next change as the file's tombstone, without the share or its key
            await using (var sockets = await ShareSockets.OpenAsync(s, memberReviewed: false))
            {
                await s.OwnerEditAsync(fileId, SecurityGroupType.Connected);
                await sockets.Member.WaitForAsync("Sam's unreviewed socket: the tombstone, no share, no key", ClientNotificationType.FileDeleted,
                    h => h.FileId == fileId && h.Shares == null && h.SharedSecretEncryptedKeyHeader.EncryptedAesKey.All(b => b == 0));
                Assert.That(sockets.Member.Seen.Where(e => e.Header?.FileId == fileId).Select(e => e.Type),
                    Has.None.EqualTo(ClientNotificationType.FileModified), "never the file itself");
            }

            var delete = await s.Sam.Drives.PeerWriter.SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive, file.GlobalTransitId);
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
            await s.Sam.Sync.DrainOutboxAsync();
            Assert.That((await s.HeaderAsync(fileId)).FileState, Is.EqualTo(FileState.Active), "his delete refused");
        }
        finally
        {
            await ReviewSamAsync(s);
        }
    }

    // An unreviewed connection counts as Authenticated, below a shared file's Connected
    private async Task UnreviewSamAsync(Arranged s)
    {
        var tenant = Host.GetTenantScope(s.Frodo.Identity.DomainName);
        await tenant.Resolve<CircleNetworkStorage>().UpdateReviewedAtAsync(s.Sam.Identity, ConnectionStatus.Connected, null);
        await tenant.Resolve<OdinContextCache>().ResetAsync();
    }

    private static async Task ReviewSamAsync(Arranged s)
    {
        var review = await new V2ConnectionNetworkClient(s.Frodo.Identity, s.Frodo.Factory).MarkReviewedAsync(s.Sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, $"review failed: {review.StatusCode}");
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
        await AssertSamGetsTheTombstoneAsync(s, fileId, "once it expired");
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

        // Only the callers who write the drive's shares get them (SelectiveShareDrives.CallerManagesShares)
        var otherApp = await AppSession.SetupAsync(s.Frodo, s.Drive, DrivePermission.All);
        Assert.That((await SharesInQueryAsync(otherApp, s, includeShares: true)).Values, Has.All.Null, "another app of the owner's");
        var otherAppById = await otherApp.Drives.Reader.GetFileHeaderAsync(s.Drive.Alias, shared, includeShares: true);
        Assert.That((otherAppById.StatusCode, otherAppById.Content?.Shares), Is.EqualTo((HttpStatusCode.OK, (List<ShareRow>?)null)),
            "another app of the owner's, by id");
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

    [Test]
    public async Task AMemberReadsAClientLockedFileWithTheirSharesKey()
    {
        var s = await ArrangeAsync();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var (fileId, locked, _) = await s.UploadClientLockedAsync(payload);
        await AssertSamSeesAsync(s, fileId, false, "before any share");

        // Sam is in a second circle of the app that shares the file too: every read takes the lowest circle id's share
        var second = await s.CircleWithKeyAsync(DrivePermission.Read | DrivePermission.Write);
        await AppCircles.AddMemberAsync(s.App, second, Identities.Sam);
        var keys = new Dictionary<Guid, byte[]> { [s.CircleId] = LockedFileKey(), [second] = LockedFileKey() };
        var write = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = keys.Select(k => s.Share(fileId, DrivePermission.Read, null, k.Value, k.Key)).ToList()
        });
        Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.OK), write.Error?.Content);
        var circle = keys.Keys.Min();

        var byId = await s.Sam.Drives.Peer.GetFileHeaderAsync(s.Frodo.Identity, s.Drive.Alias, fileId);
        Assert.That(byId.StatusCode, Is.EqualTo(HttpStatusCode.OK), byId.Error?.Content);
        var query = await s.Sam.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        });
        Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK), query.Error?.Content);

        foreach (var (header, how) in new[] { (byId.Content!, "by id"), (query.Content!.SearchResults.Single(h => h.FileId == fileId), "in a query") })
        {
            var key = header.SharedSecretEncryptedKeyHeader;
            Assert.That((key.Type, key.EncryptedAesKey, key.Iv), Is.EqualTo((EncryptionType.Share, keys[circle], locked.Iv)),
                $"{how}: the share's locked file key with the file's IV, never the drive-locked key header");
            Assert.That(header.Shares?.Select(r => (r.CircleId, r.Permissions)), Is.EqualTo(new[] { (circle, DrivePermission.Read) }),
                $"{how}: his own share, and only it");
            Assert.That(header.ServerMetadata, Is.Null, $"{how}: no ACL for an outside caller");
        }

        // A payload carries no key header: the member opens it with the file key and the payload's IV
        var read = await s.Sam.Drives.Peer.GetPayloadAsync(s.Frodo.Identity, s.Drive.Alias, fileId, payload.Key);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        Assert.That(read.Headers.Contains(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64), Is.False, "no key header for the payload");

        // The owner still gets the file's own key header, and no shares unless asked
        var owners = (await s.HeaderAsync(fileId));
        Assert.That((owners.SharedSecretEncryptedKeyHeader.Type, owners.SharedSecretEncryptedKeyHeader.EncryptedAesKey),
            Is.EqualTo((EncryptionType.ZkDrive, locked.EncryptedAesKey)));
        Assert.That(owners.Shares, Is.Null);
    }

    [TestCase(false, TestName = "AMemberWithWriteEditsASharedFileKeepingItsKey")]
    [TestCase(true, TestName = "AMemberEditsASharedFileThroughTheV2PeerUpdate")]
    public async Task AMemberWithWriteEditsASharedFile(bool v2)
    {
        var s = await ArrangeAsync();
        var (fileId, locked, fileKey) = await s.UploadClientLockedAsync();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read | DrivePermission.Write, lockedFileKey: LockedFileKey())]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var before = await s.HeaderAsync(fileId);

        var newIv = await MemberEditAsync(s, before, fileKey, "edited by Sam", v2);

        var after = await s.HeaderAsync(fileId);
        var key = after.SharedSecretEncryptedKeyHeader;
        Assert.That((key.Type, key.EncryptedAesKey, key.Iv), Is.EqualTo((EncryptionType.ZkDrive, locked.EncryptedAesKey, newIv)),
            "the file's own key header, with Sam's new IV");
        var content = new KeyHeader { AesKey = fileKey.AesKey, Iv = newIv }.Decrypt(after.FileMetadata.AppData.Content.FromBase64());
        Assert.That(content.ToStringFromUtf8Bytes(), Is.EqualTo("edited by Sam"));
        Assert.That(after.FileMetadata.VersionTag, Is.Not.EqualTo(before.FileMetadata.VersionTag), "an edit is a new version");
        Assert.That(after.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected),
            "the file keeps its ACL, so its members keep it");
        Assert.That((await s.SharesAsync(fileId)).Single().Permissions, Is.EqualTo(DrivePermission.Read | DrivePermission.Write));
        await AssertSamSeesAsync(s, fileId, true, "after his edit");
    }

    [Test]
    public async Task AMemberWithoutWriteCantEdit()
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read, lockedFileKey: LockedFileKey())]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var before = await s.HeaderAsync(fileId);

        await MemberEditAsync(s, before, fileKey, "edited by Sam");

        var after = await s.HeaderAsync(fileId);
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(before.FileMetadata.VersionTag), "a Read share edits nothing");
    }

    [TestCase(DrivePermission.Read | DrivePermission.Write, true, TestName = "AMemberWithWriteDeletesASharedFile")]
    [TestCase(DrivePermission.Read, false, TestName = "AMemberWithoutWriteCantDelete")]
    public async Task AMemberDeletesASharedFileOnlyWithWrite(DrivePermission permissions, bool deleted)
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, permissions)]);
        var gtid = (await s.HeaderAsync(fileId)).FileMetadata.GlobalTransitId!.Value;

        var request = await s.Sam.Drives.PeerWriter.SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive, gtid);
        Assert.That(request.StatusCode, Is.EqualTo(HttpStatusCode.OK), request.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();

        var after = await s.HeaderAsync(fileId);
        Assert.That(after.FileState, Is.EqualTo(deleted ? FileState.Deleted : FileState.Active));
        Assert.That((await s.SharesAsync(fileId)).Single().Permissions, Is.EqualTo(permissions),
            "a soft delete keeps the shares, so members' sync sees the tombstone");
    }

    [TestCase(DrivePermission.Read | DrivePermission.Write, true, true, TestName = "AMemberAddsAFileWithItsContentToAFolderSharedWithWrite")]
    [TestCase(DrivePermission.Read, true, false, TestName = "AMemberCantAddToAFolderSharedWithoutWrite")]
    [TestCase(DrivePermission.Read | DrivePermission.Write, false, false, TestName = "AMemberCantAddAFileOutsideAFolder")]
    public async Task AMemberAddsAFileToASharedFolder(DrivePermission folderPermissions, bool inTheFolder, bool added)
    {
        var s = await ArrangeAsync();
        var folderUniqueId = Guid.NewGuid();
        var folder = await s.Frodo.Drives.Writer.CreateNewUnencryptedFile(s.Drive.Alias, FolderMetadata(s, folderUniqueId),
            new UploadManifest { PayloadDescriptors = [] }, [], shares: [s.Share(Guid.Empty, folderPermissions)]);
        Assert.That(folder.StatusCode, Is.EqualTo(HttpStatusCode.OK), folder.Error?.Content);

        // Sam's deposit, sealed to the drive's write-only key, with its content, naming the folder by its unique id
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file",
            SamplePayloadDefinitions.GetPayloadDefinition1(), s.Drive, inTheFolder ? folderUniqueId : null);

        var received = await s.Frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, s.Drive.Alias);
        Assert.That(received.StatusCode, Is.EqualTo(added ? HttpStatusCode.OK : HttpStatusCode.NotFound), received.Error?.Content);
        if (added)
        {
            Assert.That(received.Content!.FileMetadata.Payloads, Has.Count.EqualTo(1), "its content came with it");
            Assert.That(received.Content.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Sealed), "a deposit, as sent");
            Assert.That(received.Content.FileMetadata.AppData.GroupId, Is.EqualTo(folderUniqueId));
            Assert.That(received.Content.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Owner),
                "the owner's until shared");
        }
    }

    [TestCase(true, TestName = "AMemberSharesTheirNewFileWithTheirCircle")]
    [TestCase(false, TestName = "AMemberCantShareTheirNewFileWithACircleTheyAreNotIn")]
    public async Task AMemberSharesTheirNewFile(bool theirCircle)
    {
        var s = await ArrangeAsync();
        var folderUniqueId = Guid.NewGuid();
        var folder = await s.Frodo.Drives.Writer.CreateNewUnencryptedFile(s.Drive.Alias, FolderMetadata(s, folderUniqueId),
            new UploadManifest { PayloadDescriptors = [] }, [], shares: [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Write)]);
        Assert.That(folder.StatusCode, Is.EqualTo(HttpStatusCode.OK), folder.Error?.Content);
        var circle = theirCircle ? s.CircleId : await s.CircleWithKeyAsync(DrivePermission.Read | DrivePermission.Write);

        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(s.Sam, s.Sam, s.Frodo, s.WriteOnlyPublicKeyJwk, uniqueId, "Sam's file",
            SamplePayloadDefinitions.GetPayloadDefinition1(), s.Drive, folderUniqueId,
            [s.Share(Guid.Empty, DrivePermission.Read, lockedFileKey: LockedFileKey(), circleId: circle)]);

        var received = await s.Frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, s.Drive.Alias);
        Assert.That(received.StatusCode, Is.EqualTo(theirCircle ? HttpStatusCode.OK : HttpStatusCode.NotFound), received.Error?.Content);
        if (theirCircle)
        {
            Assert.That(received.Content!.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected));
            Assert.That((await s.SharesAsync(received.Content.FileId)).Single().CircleId, Is.EqualTo(s.CircleId));
            await AssertSamSeesAsync(s, received.Content.FileId, true, "his own file, shared with his circle");
        }
    }

    [Test]
    public async Task AFileNoShareNamesAMemberOnIsMissingNotATombstone()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Connected);
        var gtid = (await s.HeaderAsync(fileId)).FileMetadata.GlobalTransitId!.Value;

        var byGtid = await s.Sam.Drives.Peer.GetFileHeaderByGtidAsync(s.Frodo.Identity, s.Drive.Alias, gtid);
        Assert.That(byGtid.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            $"nothing of a file never shared with him, not even its tombstone: {byGtid.StatusCode} {byGtid.Content?.FileState}");
    }

    [TestCase(DrivePermission.Read | DrivePermission.React, true, false, TestName = "AMemberReactsWithAShareWithReact")]
    [TestCase(DrivePermission.Read | DrivePermission.Write, false, false, TestName = "AMemberCantReactWithoutReactWhateverElseTheShareHolds")]
    [TestCase(DrivePermission.Read | DrivePermission.React, false, true, TestName = "AShareCountsOnlyWhileItsCircleGrantsTheDriveRead")]
    public async Task AMemberReactsOnlyWithAShareWithReact(DrivePermission permissions, bool reacts, bool circleLosesRead)
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, permissions)]);
        var file = await s.GtidOfAsync(fileId);
        if (circleLosesRead)
        {
            var definition = await s.Frodo.Admin.GetCircleDefinition(s.CircleId);
            definition.DriveGrants.Single().PermissionedDrive.Permission = DrivePermission.Write;
            var update = await s.Frodo.Admin.TryUpdateCircleDefinition(definition);
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        }

        var add = await s.Sam.RefitFor<IRefitOwnerTransitReaction>().AddReaction(new PeerAddReactionRequest
        {
            OdinId = s.Frodo.Identity,
            Request = new AddRemoteReactionRequest { File = file, Reaction = ":thumbsup:" }
        });
        Assert.That(add.IsSuccessStatusCode, Is.EqualTo(reacts), $"the reaction answered {add.StatusCode}: {add.Error?.Content}");

        var reactions = await s.Frodo.Drives.Reactions.GetAllReactionsAsync(s.Drive.Alias, fileId);
        Assert.That(reactions.StatusCode, Is.EqualTo(HttpStatusCode.OK), reactions.Error?.Content);
        Assert.That(reactions.Content!.Reactions.Select(r => (r.OdinId.ToString(), r.ReactionContent)),
            Is.EqualTo(reacts ? new[] { (Identities.Sam.ToString(), ":thumbsup:") } : []), "what Frodo's file holds");
    }

    [Test]
    public async Task AMemberSeesAFilesReactionsOnlyWhileTheyMayReadIt()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read)]);
        var file = await s.GtidOfAsync(fileId);
        var react = await s.Frodo.Drives.Reactions.AddReactionAsync(s.Drive.Alias, fileId, ":heart:");
        Assert.That(react.StatusCode, Is.EqualTo(HttpStatusCode.OK), react.Error?.Content);

        async Task<HttpStatusCode> SamListsAsync() => (await s.Sam.RefitFor<IRefitOwnerTransitReaction>().GetAllReactions(
            new PeerGetReactionsRequest
            {
                OdinId = s.Frodo.Identity,
                Request = new GetRemoteReactionsRequest { File = file, Cursor = "", MaxRecords = 10 }
            })).StatusCode;

        Assert.That(await SamListsAsync(), Is.EqualTo(HttpStatusCode.OK), "while shared with him");
        var withdraw = await s.Shares.WithdrawShare(s.Drive.Alias, fileId, s.CircleId);
        Assert.That(withdraw.StatusCode, Is.EqualTo(HttpStatusCode.OK), withdraw.Error?.Content);
        Assert.That(await SamListsAsync(), Is.Not.EqualTo(HttpStatusCode.OK), "once withdrawn");
    }

    [Test]
    public async Task AMembersCommentIsEncryptedWithTheFilesKeyAndReadWithTheirShare()
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var lockedFileKey = LockedFileKey();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, DrivePermission.Read | DrivePermission.Comment, lockedFileKey: lockedFileKey)]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var file = await s.GtidOfAsync(fileId);

        var commentIv = await SendCommentAsync(s, s.Sam, file, fileKey, "Sam's comment");

        // Frodo's console holds the drive's key: the comment as stored, its own IV and no key
        var frodos = await CommentsAsync(s.Frodo.Drives.Reader, s, file);
        var stored = frodos.Single();
        Assert.That((stored.SharedSecretEncryptedKeyHeader.Type, stored.SharedSecretEncryptedKeyHeader.Iv,
                stored.SharedSecretEncryptedKeyHeader.EncryptedAesKey?.Length ?? 0),
            Is.EqualTo((EncryptionType.FileKey, commentIv, 0)), "a file key header: the comment's IV only");
        Assert.That(new KeyHeader { AesKey = fileKey.AesKey, Iv = commentIv }
            .Decrypt(stored.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes(), Is.EqualTo("Sam's comment"));
        Assert.That(stored.ServerMetadata.AccessControlList.RequiredSecurityGroup, Is.EqualTo(SecurityGroupType.Connected),
            "its file's ACL, naming no one");

        // Sam reads it as he reads the file: the share's locked file key, with the comment's IV
        var sams = await SamsCommentsAsync(s, file);
        var key = sams.Single().SharedSecretEncryptedKeyHeader;
        Assert.That((sams.Single().FileState, key.Type, key.EncryptedAesKey, key.Iv),
            Is.EqualTo((FileState.Active, EncryptionType.Share, lockedFileKey, commentIv)));
        Assert.That(sams.Single().Shares, Is.Null, "the file's share is shown with the file, not its comments");
        var byId = await s.Sam.Drives.Peer.GetFileHeaderAsync(s.Frodo.Identity, s.Drive.Alias, stored.FileId, FileSystemType.Comment);
        Assert.That(byId.StatusCode, Is.EqualTo(HttpStatusCode.OK), byId.Error?.Content);

        // Pippin, connected and in no circle of the app, never; and once the share is withdrawn, the comment goes with the file
        var pippin = await LoginAsOwner(Identities.Pippin);
        var pippins = await pippin.Drives.Peer.GetFileHeaderAsync(s.Frodo.Identity, s.Drive.Alias, stored.FileId, FileSystemType.Comment);
        Assert.That(pippins.IsSuccessStatusCode, Is.False, "Pippin reads no comment");
        var withdraw = await s.Shares.WithdrawShare(s.Drive.Alias, fileId, s.CircleId);
        Assert.That(withdraw.StatusCode, Is.EqualTo(HttpStatusCode.OK), withdraw.Error?.Content);
        Assert.That((await SamsCommentsAsync(s, file)).Select(c => c.FileState), Is.EqualTo(new[] { FileState.Deleted }),
            "the tombstone, as for the file");
    }

    [TestCase(DrivePermission.Read | DrivePermission.Write | DrivePermission.React, true,
        TestName = "AMemberCantCommentWithoutCommentWhateverElseTheShareHolds")]
    [TestCase(DrivePermission.Read | DrivePermission.Comment, false, TestName = "AMembersCommentOnAnEncryptedFileMustUseItsKey")]
    public async Task AMembersCommentIsRefused(DrivePermission permissions, bool withTheFilesKey)
    {
        var s = await ArrangeAsync();
        var (fileId, _, fileKey) = await s.UploadClientLockedAsync();
        var share = await s.Shares.WriteShares(s.Drive.Alias, new WriteSharesRequest
        {
            Shares = [s.Share(fileId, permissions, lockedFileKey: LockedFileKey())]
        });
        Assert.That(share.StatusCode, Is.EqualTo(HttpStatusCode.OK), share.Error?.Content);
        var file = await s.GtidOfAsync(fileId);

        await SendCommentAsync(s, s.Sam, file, fileKey, "Sam's comment", withTheFilesKey);

        Assert.That(await CommentsAsync(s.Frodo.Drives.Reader, s, file), Is.Empty, "no comment stored");
    }

    [Test]
    public async Task OnlyACommentsAuthorDeletesIt()
    {
        var s = await ArrangeAsync();
        var fileId = await s.UploadAsync(SecurityGroupType.Owner, [s.Share(Guid.Empty, DrivePermission.Read | DrivePermission.Comment)]);
        var file = await s.GtidOfAsync(fileId);
        await SendCommentAsync(s, s.Sam, file, null, "Sam's comment");
        var comment = (await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single();
        var commentGtid = comment.FileMetadata.GlobalTransitId!.Value;

        // Pippin, in the circle too, may comment on the file, not delete Sam's comment
        await AppCircles.AddMemberAsync(s.App, s.CircleId, Identities.Pippin);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var pippins = await CommentWriter(pippin).SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive, commentGtid);
        Assert.That(pippins.StatusCode, Is.EqualTo(HttpStatusCode.OK), pippins.Error?.Content);
        await pippin.Sync.DrainOutboxAsync();
        Assert.That((await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single().FileState, Is.EqualTo(FileState.Active),
            "another member's delete");

        var sams = await CommentWriter(s.Sam).SendDeleteRequestOverPeer(s.Frodo.Identity, s.Drive, commentGtid);
        Assert.That(sams.StatusCode, Is.EqualTo(HttpStatusCode.OK), sams.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();
        Assert.That((await CommentsAsync(s.Frodo.Drives.Reader, s, file)).Single().FileState, Is.EqualTo(FileState.Deleted),
            "its author's");
    }

    private static DrivePeerWriterV2Client CommentWriter(OwnerSession caller) =>
        new(caller.Identity, caller.Factory, FileSystemType.Comment);

    /// <summary>
    /// A member's comment on Frodo's file, sent through their own server's transient drive: on an encrypted file,
    /// encrypted with the file's key and a new IV, which its key header holds alone (fileKey) unless
    /// <paramref name="withTheFilesKey"/> is false; on an unencrypted file (<paramref name="fileKey"/> null), as it is.
    /// Returns the comment's IV.
    /// </summary>
    private static async Task<byte[]?> SendCommentAsync(Arranged s, OwnerSession member, GlobalTransitIdFileIdentifier file,
        KeyHeader? fileKey, string content, bool withTheFilesKey = true)
    {
        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, content, AccessControlList.Connected);
        metadata.AllowDistribution = true;
        metadata.ReferencedFile = file;
        var transit = new TransitOptions
        {
            IsTransient = true,
            Recipients = [s.Frodo.Identity.DomainName],
            RemoteTargetDrive = s.Drive
        };

        var writer = new DriveWriterV2Client(member.Identity, member.Factory, FileSystemType.Comment);
        if (fileKey == null)
        {
            var plain = await writer.CreateNewUnencryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
                new UploadManifest { PayloadDescriptors = [] }, [], transitOptions: transit);
            Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.OK), plain.Error?.Content);
            await member.Sync.DrainOutboxAsync();
            return null;
        }

        var iv = ByteArrayUtil.GetRndByteArray(16);
        var (upload, _, _, _) = await writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata, transit,
            keyHeader: new KeyHeader { AesKey = new SensitiveByteArray(fileKey.AesKey.GetKey()), Iv = iv },
            lockedKeyHeader: withTheFilesKey ? new EncryptedKeyHeader { EncryptionVersion = 1, Type = EncryptionType.FileKey, Iv = iv } : null);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await member.Sync.DrainOutboxAsync();
        return iv;
    }

    private static async Task<List<SharedSecretEncryptedFileHeader>> CommentsAsync(DriveReaderV2Client reader, Arranged s,
        GlobalTransitIdFileIdentifier file)
    {
        var query = await reader.GetBatchAsync(s.Drive.Alias, CommentsQuery(s, file), FileSystemType.Comment);
        Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK), query.Error?.Content);
        return query.Content!.SearchResults.ToList();
    }

    private static async Task<List<SharedSecretEncryptedFileHeader>> SamsCommentsAsync(Arranged s, GlobalTransitIdFileIdentifier file)
    {
        var query = await s.Sam.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, CommentsQuery(s, file), FileSystemType.Comment);
        Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK), query.Error?.Content);
        return query.Content!.SearchResults.ToList();
    }

    // A file's comments, as clients ask for them: by the group that names it
    private static QueryBatchRequest CommentsQuery(Arranged s, GlobalTransitIdFileIdentifier file) => new()
    {
        QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive, GroupId = [file.GlobalTransitId] },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
    };

    private static UploadFileMetadata FolderMetadata(Arranged s, Guid uniqueId)
    {
        var metadata = s.Metadata(SecurityGroupType.Owner);
        metadata.AppData.UniqueId = uniqueId;
        return metadata;
    }

    /// <summary>
    /// Sam edits Frodo's file through his own server (V1 update, Locale=Peer, by global transit id), keeping the file
    /// key: a share key header with a new IV only (decision C).  Returns the new IV.
    /// </summary>
    private static async Task<byte[]> MemberEditAsync(Arranged s, SharedSecretEncryptedFileHeader file, KeyHeader fileKey, string content,
        bool v2 = false)
    {
        var newIv = ByteArrayUtil.GetRndByteArray(16);
        var metadata = s.Metadata(SecurityGroupType.Connected);
        metadata.AppData.Content = new KeyHeader { AesKey = fileKey.AesKey, Iv = newIv }.EncryptDataAes(content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;
        metadata.AllowDistribution = true;

        var keepKey = new EncryptedKeyHeader { EncryptionVersion = 1, Type = EncryptionType.Share, Iv = newIv };
        if (v2)
        {
            // The route names Frodo, the drive and the file; the body names no recipient
            var viaV2 = await s.Sam.Drives.Writer.UpdatePeerFileByGlobalTransitId(s.Frodo.Identity, s.Drive.Alias,
                file.FileMetadata.GlobalTransitId!.Value, new FileUpdateInstructionSetV2
                {
                    TransferIv = ByteArrayUtil.GetRndByteArray(16),
                    Manifest = new UploadManifest { PayloadDescriptors = [] }
                }, metadata, [], keepKey);
            Assert.That(viaV2.StatusCode, Is.EqualTo(HttpStatusCode.OK), viaV2.Error?.Content);
            await s.Sam.Sync.DrainOutboxAsync();
            return newIv;
        }

        var update = await s.Sam.V1.Drive.UpdateFile(new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = new GlobalTransitIdFileIdentifier { TargetDrive = s.Drive, GlobalTransitId = file.FileMetadata.GlobalTransitId!.Value }
                .ToFileIdentifier(),
            Recipients = [s.Frodo.Identity],
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, metadata, [], lockedKeyHeader: keepKey);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        await s.Sam.Sync.DrainOutboxAsync();
        return newIv;
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
        Assert.That(query.Content?.SearchResults.Any(h => h.FileId == fileId && h.FileState == FileState.Active) == true, Is.EqualTo(sees),
            $"{when}: in the query");
    }

    // A file no longer shared with Sam comes back in his query as a deleted file's tombstone: ids and dates, no content,
    // no key (section 8)
    private static async Task AssertSamGetsTheTombstoneAsync(Arranged s, Guid fileId, string when)
    {
        var query = await s.Sam.Drives.Peer.QueryBatchAsync(s.Frodo.Identity, s.Drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = s.Drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 100, IncludeMetadataHeader = true }
        });
        Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK), query.Error?.Content);
        var tombstone = query.Content!.SearchResults.SingleOrDefault(h => h.FileId == fileId);
        Assert.That(tombstone?.FileState, Is.EqualTo(FileState.Deleted), $"{when}: the tombstone in his query");
        Assert.That((tombstone!.FileMetadata.AppData.Content, tombstone.FileMetadata.Payloads, tombstone.Shares),
            Is.EqualTo(("", (List<PayloadDescriptor>?)null, (List<ShareRow>?)null)), $"{when}: nothing of the file");
        Assert.That(tombstone.SharedSecretEncryptedKeyHeader.EncryptedAesKey, Is.All.EqualTo(0), $"{when}: no key");
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

        var s = new Arranged(frodo, sam, app, drive, keys.DriveKey) { WriteOnlyPublicKeyJwk = keys.WriteOnlyPublicKeyJwk };
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
        public string WriteOnlyPublicKeyJwk { get; init; } = "";
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

        /// <summary>A client-locked file, with a payload when given, as an owner device writes one; its key header as sent.</summary>
        public async Task<(Guid FileId, EncryptedKeyHeader Locked, KeyHeader FileKey)> UploadClientLockedAsync(
            TestPayloadDefinition? payload = null)
        {
            var fileKey = KeyHeader.NewRandom16();
            var locked = ZkBrowser.LockFileKey(fileKey, driveKey);
            if (payload != null)
            {
                payload.Iv = ByteArrayUtil.GetRndByteArray(16);
            }

            var (upload, _, _, _) = await Frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(SecurityGroupType.Owner),
                transitOptions: null!, new UploadManifest { PayloadDescriptors = payload == null ? [] : [payload.ToPayloadDescriptor()] },
                payload == null ? [] : [payload], keyHeader: fileKey, lockedKeyHeader: locked);
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
            return (upload.Content!.FileId, locked, fileKey);
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

        /// <summary>Frodo edits the file's header from his console, sending <paramref name="group"/>; returns it before.</summary>
        public async Task<SharedSecretEncryptedFileHeader> OwnerEditAsync(Guid fileId, SecurityGroupType group)
        {
            var before = await HeaderAsync(fileId);
            var update = await Frodo.Drives.Writer.UpdateFileByFileId(Drive.Alias, fileId, new FileUpdateInstructionSetV2
            {
                Locale = UpdateLocale.Local, TransferIv = ByteArrayUtil.GetRndByteArray(16), Manifest = new UploadManifest { PayloadDescriptors = [] }
            }, Metadata(group, before.FileMetadata.VersionTag), []);
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
            return before;
        }

        public async Task<GlobalTransitIdFileIdentifier> GtidOfAsync(Guid fileId) =>
            new() { TargetDrive = Drive, GlobalTransitId = (await HeaderAsync(fileId)).FileMetadata.GlobalTransitId!.Value };

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
