using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveRead;

/// <summary>
/// query-batch with <c>resultShape: minimal</c>: one <see cref="QueryBatchFileRef"/> per row instead of a header, over
/// exactly the rows, cursor and hasMoreRows the same query returns with full headers.
/// </summary>
[TestFixture]
public class MinimalQueryBatchTests : V2Fixture
{
    [Test]
    public async Task PagesThroughTheSameRowsWithTheSameCursorsAsFullHeaders()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var files = new List<CreateFileResult>();
        for (var i = 0; i < 5; i++)
        {
            files.Add(await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, $"file {i}"));
        }

        var delete = await owner.Drives.Writer.SoftDeleteFile(files[1].DriveId, files[1].FileId);
        Assert.That(delete.IsSuccessStatusCode, Is.True, $"seed delete failed: {delete.StatusCode}");
        var archive = await owner.Drives.Writer.UpdateArchivalStatusBatch(spec.TargetDrive.Alias, new UpdateArchivalStatusBatchRequestV2
        {
            ArchivalStatus = 2,
            Files = [new UpdateArchivalStatusFileV2 { FileId = files[3].FileId, VersionTag = files[3].NewVersionTag }]
        });
        Assert.That(archive.IsSuccessStatusCode, Is.True, $"seed archive failed: {archive.StatusCode}");

        var fullPages = await ReadFeedAsync(caller, spec.TargetDrive, QueryBatchResultShape.Full);
        var minimalPages = await ReadFeedAsync(caller, spec.TargetDrive, QueryBatchResultShape.Minimal);

        Assert.That(minimalPages.Select(Describe), Is.EqualTo(fullPages.Select(Describe)));
        Assert.That(fullPages.SelectMany(p => p.SearchResults).Select(h => h.FileId),
            Is.EquivalentTo(files.Select(f => f.FileId)), "the feed should hold every seeded file");

        var headers = fullPages.SelectMany(p => p.SearchResults).ToList();
        var refs = minimalPages.SelectMany(p => p.FileRefs).ToList();
        Assert.That(refs.Select(Fields), Is.EqualTo(headers.Select(Fields)));
    }

    [Test]
    public async Task AFileHiddenByItsAclIsHiddenInEveryShape()
    {
        var spec = CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read);
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var visible = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "visible");
        await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "owner only", AccessControlList.OwnerOnly);
        var after = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "after");

        var full = await QueryAsync(caller, spec.TargetDrive, QueryBatchResultShape.Full, maxRecords: 10);
        var minimal = await QueryAsync(caller, spec.TargetDrive, QueryBatchResultShape.Minimal, maxRecords: 10);

        var expected = new[] { visible.FileId, after.FileId };
        Assert.That(full.SearchResults.Select(h => h.FileId), Is.EqualTo(expected));
        Assert.That(minimal.FileRefs.Select(r => r.FileId), Is.EqualTo(expected));
        Assert.That(minimal.CursorState, Is.EqualTo(full.CursorState));
    }

    [Test]
    public async Task MinimalLeavesOutEmptyFieldsAndOtherShapesLeaveOutFileRefs()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "plain");

        var minimal = await QueryJsonAsync(caller, spec.TargetDrive, Options(QueryBatchResultShape.Minimal));
        Assert.That(Keys(minimal), Does.Contain("fileRefs").And.Not.Contain("searchResults"), minimal.ToString());
        var row = minimal.GetProperty("fileRefs").EnumerateArray().Single();
        Assert.That(Keys(row), Is.SupersetOf(new[] { "fileId", "versionTag", "fileState", "archivalStatus", "updated" }), row.ToString());
        // An upload always gets a global transit id and a sender; this one has no unique id, group, local app data or user date
        Assert.That(Keys(row), Has.None.AnyOf("uniqueId", "groupId", "localVersionTag", "userDate"), row.ToString());

        var legacy = await QueryJsonAsync(caller, spec.TargetDrive, new QueryBatchResultOptionsRequest { MaxRecords = 10 });
        Assert.That(Keys(legacy), Does.Contain("searchResults").And.Not.Contain("fileRefs"), legacy.ToString());
    }

    [Test]
    public async Task WithoutAResultShapeTheOldFlagStillDecides()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "the content");

        var cases = new Dictionary<string, QueryBatchResultOptionsRequest>
        {
            ["flag true"] = new() { MaxRecords = 10, IncludeMetadataHeader = true },
            ["flag false"] = new() { MaxRecords = 10, IncludeMetadataHeader = false },
            ["full"] = Options(QueryBatchResultShape.Full),
            ["noContent wins over the flag"] = new() { MaxRecords = 10, IncludeMetadataHeader = true, ResultShape = QueryBatchResultShape.NoContent },
        };

        var contents = new Dictionary<string, (string Content, bool IncludeMetadataHeader)>();
        foreach (var (name, options) in cases)
        {
            var response = await caller.Drives.Reader.GetBatchAsync(spec.TargetDrive.Alias, Request(spec.TargetDrive, options));
            Assert.That(response.IsSuccessStatusCode, Is.True, $"{name}: {response.StatusCode}");
            contents[name] = (response.Content!.SearchResults.Single().FileMetadata.AppData.Content, response.Content.IncludeMetadataHeader);
        }

        Assert.That(contents, Is.EqualTo(new Dictionary<string, (string, bool)>
        {
            ["flag true"] = ("the content", true),
            ["flag false"] = ("", false),
            ["full"] = ("the content", true),
            ["noContent wins over the flag"] = ("", false),
        }));
    }

    [Test]
    public async Task ACollectionSectionCanBeMinimalOnItsOwn()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var a = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "a");
        var b = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "b");

        var response = await caller.Drives.Reader.GetBatchCollectionAsync(new QueryBatchCollectionRequestV2
        {
            MaxRecords = 3,
            Queries =
            [
                Section("minimal", spec.TargetDrive, QueryBatchResultShape.Minimal),
                Section("full", spec.TargetDrive, QueryBatchResultShape.Full)
            ]
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"collection failed: {response.StatusCode}");
        var sections = response.Content!.Results.ToDictionary(r => r.Name);
        Assert.That(sections["minimal"].FileRefs?.Select(r => r.FileId), Is.EqualTo(new[] { a.FileId, b.FileId }));
        Assert.That(sections["minimal"].SearchResults, Is.Null);
        Assert.That(sections["full"].FileRefs, Is.Null);
        // The minimal section's two rows count against the budget like headers would
        Assert.That(sections["full"].SearchResults?.Select(h => h.FileId), Is.EqualTo(new[] { a.FileId }));
    }

    [Test]
    public async Task APeerQueryRefusesMinimal()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var response = await owner.Drives.Peer.QueryBatchAsync(new OdinId(Identities.Sam), spec.TargetDrive.Alias,
            Request(spec.TargetDrive, Options(QueryBatchResultShape.Minimal)));

        DriveAsserts.AssertRejected(response, OdinClientErrorCode.InvalidQuery);
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<List<QueryBatchResponse>> ReadFeedAsync(IV2Caller caller, TargetDrive drive, QueryBatchResultShape shape)
    {
        var pages = new List<QueryBatchResponse>();
        string cursor = null;
        do
        {
            var page = await QueryAsync(caller, drive, shape, maxRecords: 2, cursor);
            pages.Add(page);
            cursor = page.CursorState;
        } while (pages[^1].HasMoreRows && pages.Count < 10);

        return pages;
    }

    private static async Task<QueryBatchResponse> QueryAsync(IV2Caller caller, TargetDrive drive, QueryBatchResultShape shape,
        int maxRecords, string cursor = null)
    {
        var options = Options(shape);
        options.MaxRecords = maxRecords;
        options.CursorState = cursor;
        var response = await caller.Drives.Reader.GetBatchAsync(drive.Alias, Request(drive, options));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"{shape} query failed: {response.StatusCode} {response.Error?.Content}");
        return response.Content!;
    }

    private static async Task<JsonElement> QueryJsonAsync(IV2Caller caller, TargetDrive drive, QueryBatchResultOptionsRequest options)
    {
        var response = await caller.Drives.Reader.GetBatchJsonAsync(drive.Alias, Request(drive, options));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"query failed: {response.StatusCode} {response.Error?.Content}");
        return response.Content;
    }

    private static QueryBatchResultOptionsRequest Options(QueryBatchResultShape shape) => new()
    {
        MaxRecords = 10,
        ResultShape = shape,
        Sorting = QueryBatchSortField.AnyChangeDate,
        Ordering = QueryBatchSortOrder.OldestFirst
    };

    private static QueryBatchRequest Request(TargetDrive drive, QueryBatchResultOptionsRequest options) => new()
    {
        QueryParams = new FileQueryParamsV1 { TargetDrive = drive, FileState = [FileState.Active, FileState.Deleted] },
        ResultOptionsRequest = options
    };

    private static CollectionQueryParamSectionV2 Section(string name, TargetDrive drive, QueryBatchResultShape shape) => new()
    {
        Name = name,
        DriveId = drive.Alias,
        QueryParams = new FileQueryParams(),
        ResultOptionsRequest = new QueryBatchCollectionSectionOptionsV2
        {
            ResultShape = shape,
            Sorting = QueryBatchSortField.AnyChangeDate,
            Ordering = QueryBatchSortOrder.OldestFirst
        }
    };

    private static string Describe(QueryBatchResponse page)
    {
        var ids = page.FileRefs?.Select(r => r.FileId) ?? page.SearchResults.Select(h => h.FileId);
        return $"[{string.Join(",", ids)}] more:{page.HasMoreRows} cursor:{page.CursorState}";
    }

    private static string Fields(QueryBatchFileRef r) =>
        $"{r.FileId} {r.VersionTag} {r.FileState} {r.ArchivalStatus} {r.FileType} {r.Created.milliseconds} {r.Updated.milliseconds} {r.FileByteCount}";

    private static string Fields(SharedSecretEncryptedFileHeader h) =>
        $"{h.FileId} {h.FileMetadata.VersionTag} {h.FileState} {h.FileMetadata.AppData.ArchivalStatus} {h.FileMetadata.AppData.FileType} " +
        $"{h.FileMetadata.Created.milliseconds} {h.FileMetadata.Updated.milliseconds} {h.FileByteCount}";

    private static List<string> Keys(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToList();
}
