using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveQuery;

/// <summary>
/// Port of <c>AppAPI/Drive/DriveQueryBatchCollectionTests</c>. The V1 batch-collection endpoint as
/// an app: three sections over three granted drives all return their file; a section naming a drive
/// the app has no grant on comes back flagged <c>InvalidDrive</c> without failing the call; a
/// section naming a drive that does not exist at all fails the whole call; and each section keeps
/// its own <c>MaxRecords</c> budget.
/// </summary>
/// <remarks>
/// The original built its app by hand (<c>ownerClient.Apps.RegisterApp</c> +
/// <c>_scaffold.CreateAppClient</c>) because its grant spans two or three drives, which
/// <see cref="CallerSpec.App(DriveSpec, DrivePermission, IReadOnlyList{int})"/> cannot express. The
/// port keeps that shape via <see cref="AppSession.SetupAsync(OwnerSession, PermissionSetGrantRequest,
/// System.Collections.Generic.List{System.Guid}, PermissionSetGrantRequest, System.Guid?)"/> — the
/// whole-grant overload — so the request stays verbatim, including the empty
/// <c>PermissionSet</c>. One live caller, so no matrix and plain <c>[Test]</c> methods; drives are
/// created through <c>owner.Admin.CreateDrive</c> with the original's
/// <c>allowAnonymousReads: false</c> and the same names, and files are seeded as the owner exactly
/// as the original's <c>UploadStandardRandomFileHeadersUsingOwnerApi</c> did. The first two tests
/// had identical ninety-line bodies differing only in how many drives the app was granted and in
/// their last two assertions; that arrange is <c>QueryThreeSectionsAsync</c>.
///
/// The two <c>V1BatchCollection…</c> tests carry their original <c>&lt;summary&gt;</c> docs: they are
/// guard rails on issue #1629, pinning V1's whole-call failure and per-section record budgets
/// against the V2 endpoint's per-section fault isolation and single request-level budget.
/// </remarks>
[TestFixture]
public class DriveQueryBatchCollectionTests : V2Fixture
{
    [Test]
    public async Task CanQueryBatchCollection()
    {
        var (result, fileIds) = await QueryThreeSectionsAsync(grantedDriveCount: 3);

        AssertSectionHasFile(result, Section1Name, fileIds[0]);
        AssertSectionHasFile(result, Section2Name, fileIds[1]);
        AssertSectionHasFile(result, Section3Name, fileIds[2]);
    }

    [Test]
    public async Task QueryBatchCollectionReturnsAvailableResults_EvenWhenNoAccessToDrive()
    {
        // pippin uploads files across 3 drives
        // his app has access to all 2 drives
        // he receives results for the 2 drives he has access to; the other one returns an error but does not fail.

        var (result, fileIds) = await QueryThreeSectionsAsync(grantedDriveCount: 2);

        AssertSectionHasFile(result, Section1Name, fileIds[0]);
        AssertSectionHasFile(result, Section2Name, fileIds[1]);

        // query 3 should return no results and should have the invalid drive flag == true
        AssertSectionIsInvalidDrive(result, Section3Name, fileIds[2]);
    }

    /// <summary>
    /// Guard rail on the V2 work in issue #1629: the V2 collection endpoint gained per-section fault
    /// isolation, but V1 must keep failing the whole call on a bad drive. Nothing pinned that before.
    /// </summary>
    [Test]
    public async Task V1BatchCollectionStillFailsWholeCallOnNonExistentDrive()
    {
        var ownerClient = await LoginAsOwner();

        var appDrive1 = await CreateDrive(ownerClient, "Good Drive 1");
        var appDrive2 = await CreateDrive(ownerClient, "Good Drive 2");

        var client = await SetupAppWithReadWriteOn(ownerClient, appDrive1, appDrive2);

        await UploadStandardRandomFileHeadersUsingOwnerApi(ownerClient, appDrive1);
        await UploadStandardRandomFileHeadersUsingOwnerApi(ownerClient, appDrive2);

        var sections = new List<CollectionQueryParamSection>()
        {
            new()
            {
                Name = "s1",
                QueryParams = new FileQueryParamsV1() { TargetDrive = appDrive1 }
            },
            new()
            {
                Name = "s2",
                QueryParams = new FileQueryParamsV1() { TargetDrive = appDrive2 }
            },
            new()
            {
                Name = "ghost",
                QueryParams = new FileQueryParamsV1() { TargetDrive = TargetDrive.NewTargetDrive() }
            }
        };

        var response = await QueryBatchCollection(client, sections);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "V1 must keep failing the whole call on an unknown drive; per-section isolation is V2-only");
        Assert.That(response.Content, Is.Null, "no partial results on a V1 whole-call failure");
    }

    /// <summary>
    /// Guard rail on the V2 work in issue #1629: V2 moved to a single request-level record budget.
    /// V1 keeps per-section budgets, each honoured independently.
    /// </summary>
    [Test]
    public async Task V1BatchCollectionKeepsPerSectionMaxRecords()
    {
        var ownerClient = await LoginAsOwner();

        var appDrive1 = await CreateDrive(ownerClient, "Budget Drive 1");
        var appDrive2 = await CreateDrive(ownerClient, "Budget Drive 2");

        var client = await SetupAppWithReadWriteOn(ownerClient, appDrive1, appDrive2);

        for (var i = 0; i < 3; i++)
        {
            await UploadStandardRandomFileHeadersUsingOwnerApi(ownerClient, appDrive1);
            await UploadStandardRandomFileHeadersUsingOwnerApi(ownerClient, appDrive2);
        }

        var sections = new List<CollectionQueryParamSection>()
        {
            new()
            {
                Name = "s1",
                QueryParams = new FileQueryParamsV1() { TargetDrive = appDrive1 },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest() { MaxRecords = 2 }
            },
            new()
            {
                Name = "s2",
                QueryParams = new FileQueryParamsV1() { TargetDrive = appDrive2 },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest() { MaxRecords = 3 }
            }
        };

        var response = await QueryBatchCollection(client, sections);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var s1 = response.Content!.Results.Single(r => r.Name == "s1");
        var s2 = response.Content.Results.Single(r => r.Name == "s2");

        // Each section gets its own budget: the second is not reduced by what the first consumed.
        Assert.That(s1.SearchResults.Count(), Is.EqualTo(2));
        Assert.That(s2.SearchResults.Count(), Is.EqualTo(3));
    }

    private const string Section1Name = "s1";
    private const string Section2Name = "s2";
    private const string Section3Name = "s3";

    /// <summary>
    /// The arrange both collection tests share: three drives with one file each, an app granted
    /// ReadWrite on the first <paramref name="grantedDriveCount"/> of them, and a three-section
    /// collection query naming all three by the unique id of the file on it. Returns the response
    /// body and the three file ids, in drive order.
    /// </summary>
    private async Task<(QueryBatchCollectionResponse Result, Guid[] FileIds)> QueryThreeSectionsAsync(int grantedDriveCount)
    {
        var ownerClient = await LoginAsOwner();

        var drives = new[]
        {
            await CreateDrive(ownerClient, "Some Drive 1"),
            await CreateDrive(ownerClient, "Some Drive 2"),
            await CreateDrive(ownerClient, "Some Drive 3")
        };

        var client = await SetupAppWithReadWriteOn(ownerClient, drives.Take(grantedDriveCount).ToArray());

        var headers = new List<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)>();
        foreach (var drive in drives)
        {
            headers.Add(await UploadStandardRandomFileHeadersUsingOwnerApi(ownerClient, drive));
        }

        var sectionNames = new[] { Section1Name, Section2Name, Section3Name };
        var sections = drives.Select((drive, i) => new CollectionQueryParamSection()
        {
            Name = sectionNames[i],
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = drive,
                ClientUniqueIdAtLeastOne = new List<Guid>() { headers[i].uploadedMetadata.AppData.UniqueId.GetValueOrDefault() }
            }
        }).ToList();

        var queryBatchResponse = await QueryBatchCollection(client, sections);
        Assert.That(queryBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var queryResult = queryBatchResponse.Content;
        Assert.That(queryResult, Is.Not.Null);
        Assert.That(queryResult!.Results.Count, Is.EqualTo(3), "Should be 3 sections");

        return (queryResult, headers.Select(h => h.uploadResult.File.FileId).ToArray());
    }

    /// <summary>The named section came back for a valid drive and carries exactly that one file.</summary>
    private static void AssertSectionHasFile(QueryBatchCollectionResponse result, string sectionName, Guid fileId)
    {
        Assert.That(result.Results, Has.Exactly(1).Matches<QueryBatchResponse>(
                r => r.Name == sectionName && r.SearchResults.Count(sr => sr.FileId == fileId) == 1),
            $"section '{sectionName}' should carry file {fileId}");

        Assert.That(result.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r => r.Name == sectionName && !r.InvalidDrive),
            $"section '{sectionName}' should be flagged as a valid drive");
    }

    /// <summary>The named section came back flagged invalid and without the file on that drive.</summary>
    private static void AssertSectionIsInvalidDrive(QueryBatchCollectionResponse result, string sectionName, Guid withheldFileId)
    {
        Assert.That(result.Results, Has.None.Matches<QueryBatchResponse>(
                r => r.Name == sectionName && r.SearchResults.Count(sr => sr.FileId == withheldFileId) == 1),
            $"section '{sectionName}' should not carry file {withheldFileId}");

        Assert.That(result.Results, Has.Exactly(1).Matches<QueryBatchResponse>(r => r.Name == sectionName && r.InvalidDrive),
            $"section '{sectionName}' should be flagged as an invalid drive");
    }

    private static Task<Refit.ApiResponse<QueryBatchCollectionResponse>> QueryBatchCollection(
        IV2Caller caller, List<CollectionQueryParamSection> querySections) =>
        caller.V1.Drive.QueryBatchCollection(new QueryBatchCollectionRequest()
        {
            Queries = querySections
        }, FileSystemType.Standard);

    private static async Task<TargetDrive> CreateDrive(OwnerSession owner, string name)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, name, allowAnonymousReads: false);
        return drive;
    }

    /// <summary>
    /// The original's hand-rolled app registration: <see cref="DrivePermission.ReadWrite"/> on each
    /// named drive and an empty <c>PermissionSet</c>.
    /// </summary>
    private static async Task<IV2Caller> SetupAppWithReadWriteOn(OwnerSession owner, params TargetDrive[] drives)
    {
        var appPermissionsGrant = new PermissionSetGrantRequest()
        {
            Drives = drives.Select(d => new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = d,
                    Permission = DrivePermission.ReadWrite
                }
            }).ToList(),
            PermissionSet = new PermissionSet()
        };

        return await AppSession.SetupAsync(owner, appPermissionsGrant);
    }

    private static async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)>
        UploadStandardRandomFileHeadersUsingOwnerApi(OwnerSession owner, TargetDrive targetDrive, AccessControlList acl = null)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            IsEncrypted = false,
            AllowDistribution = false,
            AppData = new()
            {
                FileType = 777,
                Content = $"Some json content {Guid.NewGuid()}",
                UniqueId = Guid.NewGuid(),
            },
            AccessControlList = acl ?? AccessControlList.OwnerOnly
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata, FileSystemType.Standard);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (response.Content!, fileMetadata);
    }
}
