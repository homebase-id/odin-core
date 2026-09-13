using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveTests;

/// <summary>
/// End-to-end coverage of the generic per-file TTL: the encoding travels through upload, comes back on
/// the client header, hides a file once it comes due, and resolves a pending (expire-after-first-read)
/// TTL on the first payload read - deliberately not on the header read, since link scanners prefetch.
/// </summary>
public class DirectDriveFileTtlTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Pippin });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    [Test]
    public async Task AFileWithNoTtlNeverExpires()
    {
        var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await client.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: 100);
        var uploadResult = (await client.DriveRedux.UploadNewMetadata(targetDrive, metadata)).Content;

        var header = (await client.DriveRedux.GetFileHeader(uploadResult!.File)).Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.AreEqual(FileTtl.Never, header!.FileMetadata.Ttl, "a file uploaded without a Ttl must never expire");
    }

    [Test]
    public async Task AnAbsoluteTtlAlreadyInThePastIsRejected()
    {
        // This is the guard that catches passing seconds instead of milliseconds: a seconds-since-epoch
        // value read as milliseconds lands in 1970, so the file would be born dead.
        var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await client.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = UnixTimeUtc.Now().seconds; // seconds, not milliseconds - the classic mistake

        var response = await client.DriveRedux.UploadNewMetadata(targetDrive, metadata);

        ClassicAssert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Test]
    public async Task AnAbsoluteTtlComesBackOnTheClientHeader()
    {
        var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await client.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var ttl = FileTtl.After(TimeSpan.FromDays(90)); // the chat-retention shape
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = ttl;

        var uploadResult = (await client.DriveRedux.UploadNewMetadata(targetDrive, metadata)).Content;
        var header = (await client.DriveRedux.GetFileHeader(uploadResult!.File)).Content;

        ClassicAssert.AreEqual(ttl, header!.FileMetadata.Ttl, "Ttl must survive the round trip and reach the client");
    }

    /// <summary>
    /// Exercises the whole expiry pipeline: commit schedules the job, the job fires when the Ttl comes
    /// due and soft deletes. Soft rather than hard, so a client polling query-modified learns the file
    /// went away instead of going on showing a stale copy.
    /// </summary>
    [Test]
    public async Task AnExpiredFileIsDeletedAndNoLongerQueryable()
    {
        var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await client.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        const int fileType = 4321;
        var metadata = SampleMetadataData.Create(fileType: fileType);
        metadata.Ttl = UnixTimeUtc.Now().milliseconds + 1500;

        var uploadResult = (await client.DriveRedux.UploadNewMetadata(targetDrive, metadata)).Content;
        ClassicAssert.IsNotNull(uploadResult);

        // still alive
        ClassicAssert.IsTrue((await client.DriveRedux.GetFileHeader(uploadResult!.File)).IsSuccessStatusCode);

        await Task.Delay(2500);

        // Either the job has already turned it into a tombstone, or it has not run yet and the read
        // path refuses it on its own (jobs lag; a file must never outlive its stated life just because
        // the runner is busy). Both are "no longer readable content"; which one you get is a race.
        var headerResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        if (headerResponse.StatusCode == HttpStatusCode.OK)
        {
            ClassicAssert.AreEqual(FileState.Deleted, headerResponse.Content!.FileState,
                "an expired file must be a tombstone, never active content");
            ClassicAssert.IsEmpty(headerResponse.Content.FileMetadata.Payloads ?? [],
                "a tombstone must not still carry its payloads");
        }
        else
        {
            ClassicAssert.AreEqual(HttpStatusCode.NotFound, headerResponse.StatusCode, "an expired file must not be readable");
        }

        var queryResponse = await client.DriveRedux.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = targetDrive, FileType = [fileType] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true }
        });

        ClassicAssert.IsTrue(queryResponse.IsSuccessStatusCode);

        // A tombstone deliberately DOES come back from a query - that is how a client polling for
        // changes learns the file went away, and it is the reason expiry soft deletes rather than hard
        // deletes. What must never come back is live content.
        var match = queryResponse.Content!.SearchResults.SingleOrDefault(r => r.FileId == uploadResult.File.FileId);
        if (match != null)
        {
            ClassicAssert.AreEqual(FileState.Deleted, match.FileState, "an expired file must only appear as a tombstone");
            ClassicAssert.IsEmpty(match.FileMetadata.Payloads ?? [], "a tombstone must not carry payloads");
        }
    }

    [Test]
    public async Task APendingTtlResolvesOnTheFirstPayloadReadAndNotOnTheHeaderRead()
    {
        var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await client.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var pendingTtl = FileTtl.AfterFirstRead(TimeSpan.FromHours(1));
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = pendingTtl;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var uploadResult = (await client.DriveRedux.UploadNewFile(targetDrive, metadata, manifest, payloads)).Content;
        ClassicAssert.IsNotNull(uploadResult);

        // Reading the header must NOT start the clock: mail clients and security scanners prefetch
        // links, and burning the file on a prefetch would spend it before the recipient ever looked.
        var beforeHeader = (await client.DriveRedux.GetFileHeader(uploadResult!.File)).Content;
        ClassicAssert.AreEqual(pendingTtl, beforeHeader!.FileMetadata.Ttl, "a header read must not resolve a pending Ttl");

        var payloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, payload.Key);
        ClassicAssert.IsTrue(payloadResponse.IsSuccessStatusCode);

        var afterHeader = (await client.DriveRedux.GetFileHeader(uploadResult.File)).Content;
        var resolved = afterHeader!.FileMetadata.Ttl;

        ClassicAssert.IsTrue(FileTtl.IsAbsolute(resolved), $"the first payload read must resolve the Ttl; got {resolved}");

        // now() - Ttl, and Ttl was -1h, so roughly an hour out
        var expected = UnixTimeUtc.Now().milliseconds + (long)TimeSpan.FromHours(1).TotalMilliseconds;
        ClassicAssert.IsTrue(Math.Abs(resolved - expected) < 60_000, $"expected ~{expected} but got {resolved}");

        // and it is a one-way door - a second read must not push it out again
        ClassicAssert.IsTrue((await client.DriveRedux.GetPayload(uploadResult.File, payload.Key)).IsSuccessStatusCode);
        var secondRead = (await client.DriveRedux.GetFileHeader(uploadResult.File)).Content;
        ClassicAssert.AreEqual(resolved, secondRead!.FileMetadata.Ttl, "a second read must not move an already-resolved Ttl");
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
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var callerContext = new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive());
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        // one file that never expires, one that dies in an hour
        var neverMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Anonymous);
        var neverResult = (await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, neverMetadata, manifest, payloads)).Content;

        var expiringMetadata = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.Anonymous);
        expiringMetadata.Ttl = FileTtl.After(TimeSpan.FromHours(1));
        var expiringResult = (await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, expiringMetadata, manifest, payloads)).Content;

        await callerContext.Initialize(ownerApiClient);
        var guestClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var neverMaxAge = await GetMaxAgeSeconds(guestClient, neverResult!.File, payload.Key);
        var expiringMaxAge = await GetMaxAgeSeconds(guestClient, expiringResult!.File, payload.Key);

        ClassicAssert.IsNotNull(neverMaxAge, "a non-expiring payload should still be cacheable");
        ClassicAssert.AreEqual((long)TimeSpan.FromDays(365).TotalSeconds, neverMaxAge!.Value,
            "a file with no Ttl keeps the long cache");

        ClassicAssert.IsNotNull(expiringMaxAge, "an expiring payload should still carry a Cache-Control");
        ClassicAssert.IsTrue(expiringMaxAge!.Value <= (long)TimeSpan.FromHours(1).TotalSeconds,
            $"cache must not outlive the file; got max-age={expiringMaxAge}");
        ClassicAssert.IsTrue(expiringMaxAge.Value > (long)TimeSpan.FromMinutes(50).TotalSeconds,
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
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var callerContext = new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive());
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var metadata = SampleMetadataData.Create(fileType: 102, acl: AccessControlList.Anonymous);
        metadata.Ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));
        var result = (await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, metadata, manifest, payloads)).Content;

        await callerContext.Initialize(ownerApiClient);
        var guestClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());

        var response = await guestClient.GetPayload(result!.File, payload.Key);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        ClassicAssert.IsTrue(response.Headers.TryGetValues("Cache-Control", out var values),
            "a pending-Ttl payload must carry an explicit Cache-Control");

        var maxAge = long.Parse(values.Single().Split('=').Last());
        ClassicAssert.AreEqual((long)TimeSpan.FromMinutes(20).TotalSeconds, maxAge,
            "the cache window must be exactly the life the file has left, not the unread backstop");
    }

    private static async Task<long?> GetMaxAgeSeconds(UniversalDriveApiClient client, ExternalFileIdentifier file, string payloadKey)
    {
        var response = await client.GetPayload(file, payloadKey);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode, "guest should be able to read the payload");

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
        var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var targetDrive = TargetDrive.NewTargetDrive();
        await client.DriveManager.CreateDrive(targetDrive, "Ttl Drive", "", allowAnonymousReads: false);

        var originalTtl = FileTtl.After(TimeSpan.FromDays(2));
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.Ttl = originalTtl;

        var uploadResult = (await client.DriveRedux.UploadNewMetadata(targetDrive, metadata)).Content;
        var header = (await client.DriveRedux.GetFileHeader(uploadResult!.File)).Content;

        // shorten: allowed
        var shorter = FileTtl.After(TimeSpan.FromHours(1));
        metadata.Ttl = shorter;
        var shortenResponse = await client.DriveRedux.UpdateExistingMetadata(uploadResult.File, header!.FileMetadata.VersionTag, metadata);
        ClassicAssert.IsTrue(shortenResponse.IsSuccessStatusCode);

        header = (await client.DriveRedux.GetFileHeader(uploadResult.File)).Content;
        ClassicAssert.AreEqual(shorter, header!.FileMetadata.Ttl, "an update must be able to bring death forward");

        // extend: clamped back to the shorter value rather than rejected, so that a peer update
        // carrying the sender's original Ttl cannot resurrect an expiring file
        metadata.Ttl = FileTtl.After(TimeSpan.FromDays(30));
        var extendResponse = await client.DriveRedux.UpdateExistingMetadata(uploadResult.File, header.FileMetadata.VersionTag, metadata);
        ClassicAssert.IsTrue(extendResponse.IsSuccessStatusCode);

        header = (await client.DriveRedux.GetFileHeader(uploadResult.File)).Content;
        ClassicAssert.AreEqual(shorter, header!.FileMetadata.Ttl, "an update must not push death out");
    }
}
