using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Ttl;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDriveFileTtlTests</c>. End-to-end coverage of the generic
/// per-file TTL: the encoding travels through upload, comes back on the client header, hides a file
/// once it comes due, and resolves a pending (expire-after-first-read) TTL on the first payload read
/// - deliberately not on the header read, since link scanners prefetch.
/// </summary>
/// <remarks>
/// These drive the V1 endpoints (<c>/api/owner/v1/drive/...</c>, <c>/api/guest/v1/drive/...</c>)
/// through the in-process host via <c>caller.V1.Drive</c>. The original ran everything as Pippin on
/// its own <c>WebScaffold</c>; nothing here needs a particular identity, so it uses the fixture
/// default. The two Cache-Control cases must be asked as a guest: the header is only stamped for
/// YouAuth/App callers (see <c>AddGuestApiCacheHeaderSeconds</c>), so an owner read carries none.
/// </remarks>
[TestFixture]
public class V1FileTtlTests : V2Fixture
{
    [Test]
    public async Task AFileWithNoTtlNeverExpires()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var caller = await SetupCaller(spec);
        var client = caller.V1.Drive;

        var metadata = SampleMetadataData.Create(fileType: 100);
        var uploadResult = (await client.UploadNewMetadata(spec.TargetDrive, metadata)).Content;

        var header = (await client.GetFileHeader(uploadResult!.File)).Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header!.FileMetadata.Ttl, Is.EqualTo(FileTtl.Never),
            "a file uploaded without a Ttl must never expire");
    }

    [Test]
    public async Task AnAbsoluteTtlAlreadyInThePastIsRejected()
    {
        // This is the guard that catches passing seconds instead of milliseconds: a seconds-since-epoch
        // value read as milliseconds lands in 1970, so the file would be born dead.
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var caller = await SetupCaller(spec);
        var client = caller.V1.Drive;

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = UnixTimeUtc.Now().seconds; // seconds, not milliseconds - the classic mistake

        var response = await client.UploadNewMetadata(spec.TargetDrive, metadata);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnAbsoluteTtlComesBackOnTheClientHeader()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var caller = await SetupCaller(spec);
        var client = caller.V1.Drive;

        var ttl = FileTtl.After(TimeSpan.FromDays(90)); // the chat-retention shape
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = ttl;

        var uploadResult = (await client.UploadNewMetadata(spec.TargetDrive, metadata)).Content;
        var header = (await client.GetFileHeader(uploadResult!.File)).Content;

        Assert.That(header!.FileMetadata.Ttl, Is.EqualTo(ttl),
            "Ttl must survive the round trip and reach the client");
    }

    /// <summary>
    /// Exercises the whole expiry pipeline: commit schedules the job, the job fires when the Ttl comes
    /// due and soft deletes. Soft rather than hard, so a client polling query-modified learns the file
    /// went away instead of going on showing a stale copy.
    /// </summary>
    [Test]
    public async Task AnExpiredFileIsDeletedAndNoLongerQueryable()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var caller = await SetupCaller(spec);
        var client = caller.V1.Drive;

        const int fileType = 4321;
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.Ttl = UnixTimeUtc.Now().milliseconds + 1500;

        var uploadResult = (await client.UploadNewMetadata(spec.TargetDrive, metadata)).Content;
        Assert.That(uploadResult, Is.Not.Null);

        // still alive
        Assert.That((await client.GetFileHeader(uploadResult!.File)).IsSuccessStatusCode, Is.True);

        // Not a poll: the Ttl above is wall-clock, so this is waiting for the file's stated life to
        // run out, not for a background worker to notice.
        await Task.Delay(2500);

        // Either the job has already turned it into a tombstone, or it has not run yet and the read
        // path refuses it on its own (jobs lag; a file must never outlive its stated life just because
        // the runner is busy). Both are "no longer readable content"; which one you get is a race.
        var headerResponse = await client.GetFileHeader(uploadResult.File);
        if (headerResponse.StatusCode == HttpStatusCode.OK)
        {
            Assert.That(headerResponse.Content!.FileState, Is.EqualTo(FileState.Deleted),
                "an expired file must be a tombstone, never active content");
            Assert.That(headerResponse.Content.FileMetadata.Payloads ?? [], Is.Empty,
                "a tombstone must not still carry its payloads");
        }
        else
        {
            Assert.That(headerResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                "an expired file must not be readable");
        }

        var queryResponse = await client.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = spec.TargetDrive, FileType = [fileType] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });

        Assert.That(queryResponse.IsSuccessStatusCode, Is.True);

        // A tombstone deliberately DOES come back from a query - that is how a client polling for
        // changes learns the file went away, and it is the reason expiry soft deletes rather than hard
        // deletes. What must never come back is live content.
        var match = queryResponse.Content!.SearchResults.SingleOrDefault(r => r.FileId == uploadResult.File.FileId);
        if (match != null)
        {
            Assert.That(match.FileState, Is.EqualTo(FileState.Deleted),
                "an expired file must only appear as a tombstone");
            Assert.That(match.FileMetadata.Payloads ?? [], Is.Empty, "a tombstone must not carry payloads");
        }
    }

    [Test]
    public async Task APendingTtlResolvesOnTheFirstPayloadReadAndNotOnTheHeaderRead()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var caller = await SetupCaller(spec);
        var client = caller.V1.Drive;

        var pendingTtl = FileTtl.AfterFirstRead(TimeSpan.FromHours(1));
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = pendingTtl;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var uploadResult = (await client.UploadNewFile(spec.TargetDrive, metadata, manifest, payloads)).Content;
        Assert.That(uploadResult, Is.Not.Null);

        // Reading the header must NOT start the clock: mail clients and security scanners prefetch
        // links, and burning the file on a prefetch would spend it before the recipient ever looked.
        var beforeHeader = (await client.GetFileHeader(uploadResult!.File)).Content;
        Assert.That(beforeHeader!.FileMetadata.Ttl, Is.EqualTo(pendingTtl),
            "a header read must not resolve a pending Ttl");

        var payloadResponse = await client.GetPayload(uploadResult.File, payload.Key);
        Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);

        var afterHeader = (await client.GetFileHeader(uploadResult.File)).Content;
        var resolved = afterHeader!.FileMetadata.Ttl;

        Assert.That(FileTtl.IsAbsolute(resolved), Is.True, $"the first payload read must resolve the Ttl; got {resolved}");

        // now() - Ttl, and Ttl was -1h, so roughly an hour out
        var expected = UnixTimeUtc.Now().milliseconds + (long)TimeSpan.FromHours(1).TotalMilliseconds;
        Assert.That(Math.Abs(resolved - expected), Is.LessThan(60_000), $"expected ~{expected} but got {resolved}");

        // and it is a one-way door - a second read must not push it out again
        Assert.That((await client.GetPayload(uploadResult.File, payload.Key)).IsSuccessStatusCode, Is.True);
        var secondRead = (await client.GetFileHeader(uploadResult.File)).Content;
        Assert.That(secondRead!.FileMetadata.Ttl, Is.EqualTo(resolved),
            "a second read must not move an already-resolved Ttl");
    }

    /// <summary>
    /// The one-year Cache-Control default is wrong for anything that expires: the file would be
    /// deleted on schedule and go on being served from browser and edge caches long afterwards.
    ///
    /// Note the header is only stamped for YouAuth/App callers (see AddGuestApiCacheHeaderSeconds),
    /// so this has to be asked as a guest - an owner-authenticated read gets no Cache-Control at all.
    /// </summary>
    [Test]
    public async Task PayloadCacheLifetimeIsClampedToTheFilesRemainingLife()
    {
        var spec = CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Read);
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDrive = owner.V1.Drive;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        // one file that never expires, one that dies in an hour
        var neverMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var neverResult = (await ownerDrive.UploadNewFile(spec.TargetDrive, neverMetadata, manifest, payloads)).Content;

        var expiringMetadata = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        expiringMetadata.Ttl = FileTtl.After(TimeSpan.FromHours(1));
        var expiringResult = (await ownerDrive.UploadNewFile(spec.TargetDrive, expiringMetadata, manifest, payloads)).Content;

        var guestClient = caller.V1.Drive;

        var neverMaxAge = await GetMaxAgeSeconds(guestClient, neverResult!.File, payload.Key);
        var expiringMaxAge = await GetMaxAgeSeconds(guestClient, expiringResult!.File, payload.Key);

        Assert.That(neverMaxAge, Is.Not.Null, "a non-expiring payload should still be cacheable");
        Assert.That(neverMaxAge!.Value, Is.EqualTo((long)TimeSpan.FromDays(365).TotalSeconds),
            "a file with no Ttl keeps the long cache");

        Assert.That(expiringMaxAge, Is.Not.Null, "an expiring payload should still carry a Cache-Control");
        Assert.That(expiringMaxAge!.Value, Is.LessThanOrEqualTo((long)TimeSpan.FromHours(1).TotalSeconds),
            $"cache must not outlive the file; got max-age={expiringMaxAge}");
        Assert.That(expiringMaxAge.Value, Is.GreaterThan((long)TimeSpan.FromMinutes(50).TotalSeconds),
            $"cache should still cover most of the remaining life; got max-age={expiringMaxAge}");
    }

    /// <summary>
    /// Serving a pending (expire-after-first-read) payload starts its clock, so the response may be
    /// cached for exactly the window the file now has left - no longer. That is what keeps a CDN read
    /// equivalent to a direct one: the edge copy dies when the file does.
    /// </summary>
    [Test]
    public async Task APendingTtlPayloadIsCachedOnlyForTheWindowTheReadJustStarted()
    {
        var spec = CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Read);
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDrive = owner.V1.Drive;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var metadata = SampleMetadataData.Create(fileType: 102, acl: AccessControlList.Anonymous);
        metadata.Ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));
        var result = (await ownerDrive.UploadNewFile(spec.TargetDrive, metadata, manifest, payloads)).Content;

        var guestClient = caller.V1.Drive;

        var response = await guestClient.GetPayload(result!.File, payload.Key);
        Assert.That(response.IsSuccessStatusCode, Is.True);

        Assert.That(response.Headers.TryGetValues("Cache-Control", out var values), Is.True,
            "a pending-Ttl payload must carry an explicit Cache-Control");

        var maxAge = long.Parse(values!.Single().Split('=').Last());
        Assert.That(maxAge, Is.EqualTo((long)TimeSpan.FromMinutes(20).TotalSeconds),
            "the cache window must be exactly the life the file has left, not the unread backstop");
    }

    private static async Task<long?> GetMaxAgeSeconds(UniversalDriveApiClient client, ExternalFileIdentifier file, string payloadKey)
    {
        var response = await client.GetPayload(file, payloadKey);
        Assert.That(response.IsSuccessStatusCode, Is.True, "guest should be able to read the payload");

        if (!response.Headers.TryGetValues("Cache-Control", out var values))
        {
            return null;
        }

        var maxAge = values.Single().Split('=').Last();
        return long.Parse(maxAge);
    }

    [Test]
    public async Task AnUpdateMayShortenATtlButNotExtendIt()
    {
        var spec = CallerSpec.Owner(DriveSpec.Secured());
        var caller = await SetupCaller(spec);
        var client = caller.V1.Drive;

        var originalTtl = FileTtl.After(TimeSpan.FromDays(2));
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = originalTtl;

        var uploadResult = (await client.UploadNewMetadata(spec.TargetDrive, metadata)).Content;
        var header = (await client.GetFileHeader(uploadResult!.File)).Content;

        // shorten: allowed
        var shorter = FileTtl.After(TimeSpan.FromHours(1));
        metadata.Ttl = shorter;
        var shortenResponse = await client.UpdateExistingMetadata(uploadResult.File, header!.FileMetadata.VersionTag, metadata);
        Assert.That(shortenResponse.IsSuccessStatusCode, Is.True);

        header = (await client.GetFileHeader(uploadResult.File)).Content;
        Assert.That(header!.FileMetadata.Ttl, Is.EqualTo(shorter), "an update must be able to bring death forward");

        // extend: clamped back to the shorter value rather than rejected, so that a peer update
        // carrying the sender's original Ttl cannot resurrect an expiring file
        metadata.Ttl = FileTtl.After(TimeSpan.FromDays(30));
        var extendResponse = await client.UpdateExistingMetadata(uploadResult.File, header.FileMetadata.VersionTag, metadata);
        Assert.That(extendResponse.IsSuccessStatusCode, Is.True);

        header = (await client.GetFileHeader(uploadResult.File)).Content;
        Assert.That(header!.FileMetadata.Ttl, Is.EqualTo(shorter), "an update must not push death out");
    }
}
