using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.DriveRead;

/// <summary>
/// Behaviour of <c>POST /api/v2/drives/query-batch-collection</c> that is V2-only: per-section fault
/// isolation, the per-section <see cref="QueryBatchSectionStatus"/>, the request-level record budget, and
/// per-section <c>fileSystemType</c>.  The V1 collection endpoint keeps its whole-call-fails semantics and
/// is pinned separately in <c>Odin.Hosting.Tests/AppAPI/Drive/DriveQueryBatchCollectionTests</c>.
///
/// The happy-path caller fan-out (Owner / App / Guest × anon / secured) lives in
/// <see cref="QueryBatchTests.CanQueryBatchCollection"/>; this fixture is about the degraded paths.
/// </summary>
[TestFixture]
public class QueryBatchCollectionTests : V2Fixture
{
    private const int FileType = 4200;

    // ---------------------------------------------------------------------------------------------
    // R1/R2 — a section-level fault never fails the collection
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task NonExistentDriveSectionDoesNotFailCollection()
    {
        // The case that motivated the change: a stale drive id in the client's registry used to 500 the
        // whole sync cycle.
        var owner = await LoginAsOwner(Identities.Frodo);
        var driveA = await CreateDriveAsync(owner);
        var driveB = await CreateDriveAsync(owner);

        var fileA = await UploadAsync(owner, driveA);
        var fileB = await UploadAsync(owner, driveB);

        var response = await QueryAsync(owner, 100,
            Section("a", driveA.Alias),
            Section("b", driveB.Alias),
            Section("ghost", Guid.NewGuid()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = response.Content!.Results;
        Assert.That(results.Count, Is.EqualTo(3));

        AssertOk(results[0], "a", fileA);
        AssertOk(results[1], "b", fileB);

        Assert.That(results[2].Name, Is.EqualTo("ghost"));
        Assert.That(results[2].Status, Is.EqualTo(QueryBatchSectionStatus.DriveNotFound));
        Assert.That(results[2].InvalidDrive, Is.True, "the legacy flag must stay populated for old clients");
        Assert.That(results[2].ErrorCode, Is.EqualTo(OdinClientErrorCode.InvalidDrive));
        Assert.That(results[2].SearchResults, Is.Empty);
    }

    [Test]
    public async Task NonExistentDriveDoesNotReturn500()
    {
        // Regression for the `theDrive!.TargetDriveInfo` null-deref in the old V2 controller: GetDriveAsync
        // defaults to failIfInvalid:false, so an unknown drive returned null and NRE'd into an opaque 500.
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await QueryAsync(owner, 100, Section("ghost", Guid.NewGuid()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"expected a driveNotFound section, not {(int)response.StatusCode}");
        Assert.That(response.Content!.Results.Single().Status, Is.EqualTo(QueryBatchSectionStatus.DriveNotFound));
    }

    [Test]
    public async Task ArchivedDriveSectionDoesNotFailCollection()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var granted = await CreateDriveAsync(owner);
        var archived = await CreateDriveAsync(owner);
        var file = await UploadAsync(owner, granted);

        await owner.Admin.SetArchiveFlag(archived, archived: true);

        // Archived drives are only invisible to callers without the master key, so the caller has to be an
        // app rather than the owner.
        var app = await AppSession.SetupAsync(owner, granted, DrivePermission.Read);

        var response = await QueryAsync(app, 100,
            Section("granted", granted.Alias),
            Section("archived", archived.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = response.Content!.Results;
        AssertOk(results[0], "granted", file);

        Assert.That(results[1].Status, Is.EqualTo(QueryBatchSectionStatus.DriveArchived));
        Assert.That(results[1].InvalidDrive, Is.True);
        Assert.That(results[1].SearchResults, Is.Empty);
    }

    [Test]
    public async Task UngrantedDriveSectionReportsNoReadGrant()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var granted = await CreateDriveAsync(owner);
        var ungranted = await CreateDriveAsync(owner, allowAnonymousReads: false);
        var file = await UploadAsync(owner, granted);
        await UploadAsync(owner, ungranted);

        var app = await AppSession.SetupAsync(owner, granted, DrivePermission.Read);

        var response = await QueryAsync(app, 100,
            Section("granted", granted.Alias),
            Section("ungranted", ungranted.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = response.Content!.Results;
        AssertOk(results[0], "granted", file);

        Assert.That(results[1].Status, Is.EqualTo(QueryBatchSectionStatus.NoReadGrant));
        Assert.That(results[1].InvalidDrive, Is.True,
            "invalidDrive keeps its old meaning so a client reading only that flag still works");
        Assert.That(results[1].SearchResults, Is.Empty);
    }

    [Test]
    public async Task MalformedCursorRestartsSectionWithoutError()
    {
        // Pins the silent reset in QueryBatchCursor(string): an unparseable cursor starts the section over
        // rather than failing it.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        var file = await UploadAsync(owner, drive);

        var section = Section("s", drive.Alias);
        section.ResultOptionsRequest.CursorState = "this-is-not-a-cursor";

        var response = await QueryAsync(owner, 100, section);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOk(response.Content!.Results.Single(), "s", file);
    }

    // ---------------------------------------------------------------------------------------------
    // Whole-call 400s — the only faults that are not per-section
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task DuplicateSectionNamesReturns400()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);

        var response = await QueryAsync(owner, 100,
            Section("dupe", drive.Alias),
            Section("dupe", drive.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task EmptySectionNameReturns400()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);

        var response = await QueryAsync(owner, 100, Section("", drive.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task NullQueriesReturns400()
    {
        // Used to NRE into a 500.
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await owner.Drives.Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries = null,
            MaxRecords = 100
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task NullQueryParamsReturns400()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);

        var section = Section("s", drive.Alias);
        section.QueryParams = null;

        var response = await QueryAsync(owner, 100, section);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task EmptyQueriesListReturns200EmptyResults()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var response = await QueryAsync(owner, 100);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.Results, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(null)]
    public async Task BudgetLessThanOneReturns400(int? maxRecords)
    {
        // maxRecords:0 used to reach QueryBatchAsync's `noOfItems < 1` guard, which throws
        // OdinSystemException — unmapped by the middleware, so a 500 for plainly bad client input.
        // `null` covers the omitted-field case, which binds to the int default of 0.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);

        var response = await owner.Drives.Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries = [Section("s", drive.Alias)],
            MaxRecords = maxRecords ?? 0
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // ---------------------------------------------------------------------------------------------
    // R3 — request-level record budget, greedy in-order fill
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task GlobalBudgetStopsAfterFirstDriveFillsIt()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var driveA = await CreateDriveAsync(owner);
        var driveB = await CreateDriveAsync(owner);
        var driveC = await CreateDriveAsync(owner);

        await UploadManyAsync(owner, driveA, 5);
        await UploadManyAsync(owner, driveB, 2);
        await UploadManyAsync(owner, driveC, 2);

        var response = await QueryAsync(owner, maxRecords: 3,
            Section("a", driveA.Alias),
            Section("b", driveB.Alias),
            Section("c", driveC.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var results = response.Content!.Results;

        Assert.That(results[0].Status, Is.EqualTo(QueryBatchSectionStatus.Ok));
        Assert.That(results[0].SearchResults.Count(), Is.EqualTo(3), "a should consume the whole budget");
        Assert.That(results[0].HasMoreRows, Is.True);

        foreach (var skipped in new[] { results[1], results[2] })
        {
            Assert.That(skipped.Status, Is.EqualTo(QueryBatchSectionStatus.BudgetExhausted));
            Assert.That(skipped.SearchResults, Is.Empty);
            Assert.That(skipped.HasMoreRows, Is.True, "the caller loops until every section says false");
            Assert.That(skipped.InvalidDrive, Is.False, "budget exhaustion is not a drive problem");
        }
    }

    [Test]
    public async Task GlobalBudgetSpillsToNextSections()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var driveA = await CreateDriveAsync(owner);
        var driveB = await CreateDriveAsync(owner);
        var driveC = await CreateDriveAsync(owner);

        await UploadManyAsync(owner, driveA, 4);
        await UploadManyAsync(owner, driveB, 1);
        await UploadManyAsync(owner, driveC, 6);

        // budget 10: a returns 4 (remaining 6), b returns 1 (remaining 5), c fills the rest.
        var response = await QueryAsync(owner, maxRecords: 10,
            Section("a", driveA.Alias),
            Section("b", driveB.Alias),
            Section("c", driveC.Alias));

        var results = response.Content!.Results;
        Assert.That(results[0].SearchResults.Count(), Is.EqualTo(4));
        Assert.That(results[0].HasMoreRows, Is.False);
        Assert.That(results[1].SearchResults.Count(), Is.EqualTo(1));
        Assert.That(results[1].HasMoreRows, Is.False);
        Assert.That(results[2].SearchResults.Count(), Is.EqualTo(5), "c is capped by what is left of the budget");
        Assert.That(results[2].HasMoreRows, Is.True);
    }

    [Test]
    public async Task BudgetExhaustedSectionEchoesSubmittedCursorVerbatim()
    {
        // The one that matters most: the client re-sends this cursor next round, so losing or rewriting it
        // silently drops or replays records.
        var owner = await LoginAsOwner(Identities.Frodo);
        var driveA = await CreateDriveAsync(owner);
        var driveB = await CreateDriveAsync(owner);
        await UploadManyAsync(owner, driveA, 3);
        await UploadManyAsync(owner, driveB, 3);

        // Take a real cursor for driveB by running it on its own first.
        var seed = await QueryAsync(owner, maxRecords: 1, Section("b", driveB.Alias));
        var submittedCursor = seed.Content!.Results.Single().CursorState;
        Assert.That(submittedCursor, Is.Not.Null.And.Not.Empty);

        var sectionB = Section("b", driveB.Alias);
        sectionB.ResultOptionsRequest.CursorState = submittedCursor;

        var response = await QueryAsync(owner, maxRecords: 3,
            Section("a", driveA.Alias),
            sectionB);

        var skipped = response.Content!.Results[1];
        Assert.That(skipped.Status, Is.EqualTo(QueryBatchSectionStatus.BudgetExhausted));
        Assert.That(skipped.CursorState, Is.EqualTo(submittedCursor));
    }

    [Test]
    public async Task BudgetRoundTripDrainsAllSections()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var driveA = await CreateDriveAsync(owner);
        var driveB = await CreateDriveAsync(owner);
        var driveC = await CreateDriveAsync(owner);

        var expected = new List<Guid>();
        expected.AddRange(await UploadManyAsync(owner, driveA, 5));
        expected.AddRange(await UploadManyAsync(owner, driveB, 4));
        expected.AddRange(await UploadManyAsync(owner, driveC, 3));

        var sections = new[]
        {
            Section("a", driveA.Alias),
            Section("b", driveB.Alias),
            Section("c", driveC.Alias)
        };

        var seen = new List<Guid>();
        var rounds = 0;

        while (true)
        {
            var response = await QueryAsync(owner, maxRecords: 4, sections);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var results = response.Content!.Results;
            foreach (var (section, result) in sections.Zip(results))
            {
                Assert.That(result.Name, Is.EqualTo(section.Name), "sections must come back in submitted order");
                seen.AddRange(result.SearchResults.Select(r => r.FileId));

                // Carry the cursor forward — for a budgetExhausted section this is the one we submitted.
                section.ResultOptionsRequest.CursorState = result.CursorState;
            }

            if (results.All(r => !r.HasMoreRows)) break;

            Assert.That(++rounds, Is.LessThan(20), "the loop should converge, not spin");
        }

        Assert.That(seen, Is.Unique, "no file may be returned twice across the round trip");
        Assert.That(seen.OrderBy(x => x), Is.EqualTo(expected.OrderBy(x => x)),
            "every file must be returned exactly once");
    }

    [Test]
    public async Task FailedSectionConsumesNoBudget()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var driveA = await CreateDriveAsync(owner);
        var driveC = await CreateDriveAsync(owner);
        await UploadManyAsync(owner, driveA, 2);
        await UploadManyAsync(owner, driveC, 5);

        var response = await QueryAsync(owner, maxRecords: 6,
            Section("a", driveA.Alias),
            Section("ghost", Guid.NewGuid()),
            Section("c", driveC.Alias));

        var results = response.Content!.Results;
        Assert.That(results[0].SearchResults.Count(), Is.EqualTo(2));
        Assert.That(results[1].Status, Is.EqualTo(QueryBatchSectionStatus.DriveNotFound));
        Assert.That(results[2].SearchResults.Count(), Is.EqualTo(4),
            "the failed section must not have eaten any of the remaining budget");
    }

    [Test]
    public async Task SectionOrderIsPreservedAndNotReordered()
    {
        // Section order is caller-controlled priority. A drive with a large backlog consuming several
        // consecutive budgets is intended; a client that wants fairness rotates the order itself.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drives = new List<TargetDrive>();
        for (var i = 0; i < 4; i++)
        {
            drives.Add(await CreateDriveAsync(owner));
        }

        var sections = drives.Select((d, i) => Section($"s{i}", d.Alias)).ToArray();
        var response = await QueryAsync(owner, maxRecords: 10, sections);

        Assert.That(response.Content!.Results.Select(r => r.Name),
            Is.EqualTo(new[] { "s0", "s1", "s2", "s3" }));
    }

    [Test]
    public async Task BudgetAboveCeilingIsClampedNotRejected()
    {
        // An over-large ask is a client that wants everything, not a client error -- it gets the ceiling and
        // pages through via hasMoreRows. The exact clamp arithmetic is unit-tested in
        // Odin.Services.Tests QueryBatchWireShapeTests.RecordBudgetIsClampedToTheCeiling; this pins that the
        // endpoint accepts the request at all rather than 400ing or blowing up on int.MaxValue.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        var file = await UploadAsync(owner, drive);

        var response = await QueryAsync(owner, maxRecords: int.MaxValue, Section("s", drive.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        AssertOk(response.Content!.Results.Single(), "s", file);
    }

    // ---------------------------------------------------------------------------------------------
    // Per-section fileSystemType
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task SectionFileSystemTypeIsHonouredPerSection()
    {
        // One collection, three sections on the same drive: unset (falls back to the request-level default,
        // Standard), explicit Standard, and explicit Comment. Only the Comment section misses — a Standard
        // file is not in the comment store. Standalone comment files need a parent reference, so this
        // asserts store routing rather than authoring a comment.
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        var file = await UploadAsync(owner, drive);

        var standard = Section("standard", drive.Alias);
        standard.FileSystemType = FileSystemType.Standard;

        var comment = Section("comment", drive.Alias);
        comment.FileSystemType = FileSystemType.Comment;

        var response = await QueryAsync(owner, 100,
            Section("default", drive.Alias),
            standard,
            comment);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var results = response.Content!.Results;

        AssertOk(results[0], "default", file);
        AssertOk(results[1], "standard", file);

        Assert.That(results[2].Status, Is.EqualTo(QueryBatchSectionStatus.Ok));
        Assert.That(results[2].SearchResults.Any(r => r.FileId == file), Is.False,
            "a Standard file must not surface in a Comment-filesystem section");
    }

    // ---------------------------------------------------------------------------------------------
    // Parity with the single-query V2 endpoint
    // ---------------------------------------------------------------------------------------------

    [Test]
    public async Task CollectionMatchesSingleQueryBatchForSameParams()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        await UploadManyAsync(owner, drive, 4);

        var single = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive, FileType = [FileType] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 2 }
        });
        Assert.That(single.IsSuccessStatusCode, Is.True);

        var collection = await QueryAsync(owner, maxRecords: 2, Section("s", drive.Alias));
        var section = collection.Content!.Results.Single();

        Assert.That(section.SearchResults.Select(r => r.FileId),
            Is.EqualTo(single.Content!.SearchResults.Select(r => r.FileId)));
        Assert.That(section.CursorState, Is.EqualTo(single.Content.CursorState));
        Assert.That(section.HasMoreRows, Is.EqualTo(single.Content.HasMoreRows));
    }

    [Test]
    public async Task AnonymousReadableDriveWithoutGrantReturnsDataInCollection()
    {
        // R4. The V1 collection gates on HasDrivePermission(Read) and so reports invalidDrive here, while a
        // single query-batch returns the data — because AssertCanReadDriveAsync short-circuits on
        // AllowAnonymousReads. The two V2 endpoints must agree; they now share the same gate.
        var owner = await LoginAsOwner(Identities.Frodo);
        var granted = await CreateDriveAsync(owner);
        var anonNoGrant = await CreateDriveAsync(owner, allowAnonymousReads: true);
        var anonFile = await UploadAsync(owner, anonNoGrant);

        var app = await AppSession.SetupAsync(owner, granted, DrivePermission.Read);

        var response = await QueryAsync(app, 100, Section("anon", anonNoGrant.Alias));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var section = response.Content!.Results.Single();
        Assert.That(section.Status, Is.EqualTo(QueryBatchSectionStatus.Ok));
        Assert.That(section.InvalidDrive, Is.False);
        Assert.That(section.SearchResults.Any(r => r.FileId == anonFile), Is.True);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static CollectionQueryParamSectionV2 Section(string name, Guid driveId) =>
        new()
        {
            Name = name,
            DriveId = driveId,
            QueryParams = new FileQueryParams { FileType = [FileType] },
            ResultOptionsRequest = new QueryBatchCollectionSectionOptionsV2()
        };

    private static Task<Refit.ApiResponse<QueryBatchCollectionResponseV2>> QueryAsync(
        IV2Caller caller,
        int maxRecords,
        params CollectionQueryParamSectionV2[] sections) =>
        caller.Drives.Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            Queries = sections.ToList(),
            MaxRecords = maxRecords
        });

    private static async Task<TargetDrive> CreateDriveAsync(OwnerSession owner, bool allowAnonymousReads = true)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "collection test drive", allowAnonymousReads);
        return drive;
    }

    private static async Task<Guid> UploadAsync(OwnerSession owner, TargetDrive drive)
    {
        var metadata = SampleMetadataData.Create(fileType: FileType);
        // Drive-level ACL governs access in these tests; the file itself is always readable.
        metadata.AccessControlList = AccessControlList.Anonymous;
        var response = await owner.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"owner upload failed: {response.StatusCode}");
        return response.Content!.FileId;
    }

    private static async Task<List<Guid>> UploadManyAsync(OwnerSession owner, TargetDrive drive, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            ids.Add(await UploadAsync(owner, drive));
        }

        return ids;
    }

    private static void AssertOk(QueryBatchCollectionSectionV2 section, string name, Guid expectedFileId)
    {
        Assert.That(section.Name, Is.EqualTo(name));
        Assert.That(section.Status, Is.EqualTo(QueryBatchSectionStatus.Ok));
        Assert.That(section.InvalidDrive, Is.False);
        Assert.That(section.ErrorMessage, Is.Null);
        Assert.That(section.SearchResults.SingleOrDefault(r => r.FileId == expectedFileId), Is.Not.Null);
    }
}
