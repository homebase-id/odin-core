using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveQuery;

/// <summary>
/// Port of <c>AppAPI/Drive/DriveQueryAppTests</c>. Query-batch as an app over its own drive: by tag,
/// by archival status, the default (unfiltered) query and what it returns on each header, and the
/// redacted form that omits content when <c>IncludeMetadataHeader</c> is false.
/// </summary>
/// <remarks>
/// The original's arrange was <c>_scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, metadata,
/// options)</c>: owner creates a drive (anonymous reads off), registers an app holding
/// <see cref="DrivePermission.All"/> on it plus the connection-read and transit-write permission
/// keys, and the app uploads one encrypted file with a payload. That is
/// <see cref="CallerSpec.SampleAppWithConnectionReads"/> plus
/// <see cref="AppFileUploads.UploadEncryptedAsync"/> here. One live caller, so no matrix and plain
/// <c>[Test]</c> methods. The circle-with-drive the original also created is dropped — nothing
/// reads it without transit recipients — as is
/// <c>TransitTestUtilsOptions.DisconnectIdentitiesAfterTransfer</c>, which only runs for recipients
/// there are none of.
/// <para>
/// Queries go through <see cref="IUniversalDriveHttpClientApi"/>, which addresses the same V1
/// endpoints the original's <c>IDriveTestHttpClientForApps</c> did, resolved against
/// <c>/api/apps/v1</c> by the app factory's path handler.
/// </para>
/// <para>
/// <b>Carried quirk — <see cref="CanQueryDriveModifiedArchivedItems"/> does not test what its name
/// says.</b> Each <c>CreateAppAndUploadFileMetadata</c> call minted a <i>new app and a new drive</i>,
/// so the un-archived file and the archived file never shared a drive. The query runs against the
/// second drive, which holds only the archived file, and its <c>SearchResults.Single()</c> would
/// pass with or without the archival-status filter. Reproduced faithfully (two separate app
/// callers, two separate drives) rather than fixed inside a port.
/// </para>
/// </remarks>
[TestFixture]
public class DriveQueryAppTests : V2Fixture
{
    [Test]
    public async Task CanQueryBatchByOneTag()
    {
        Guid tag = Guid.NewGuid();
        List<Guid> tags = new List<Guid>() { tag };

        var uploadFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = tags
            }
        };

        var spec = CallerSpec.SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);
        await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, uploadFileMetadata,
            payloadData: "some payload data for good measure");

        var request = new QueryBatchRequest()
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = spec.TargetDrive,
                TagsMatchAtLeastOne = tags
            },

            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = false
            }
        };

        var response = await caller.V1.Drive.QueryBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SearchResults.Single(item => item.FileMetadata.AppData.Tags.Any(t => t == tag)), Is.Not.Null);
    }

    [Test]
    public async Task CanQueryBatchByArchivalStatus()
    {
        const int archivalStatus = 1;
        var uploadFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                ArchivalStatus = archivalStatus
            }
        };

        var spec = CallerSpec.SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);
        await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, uploadFileMetadata,
            payloadData: "some payload data for good measure");

        var request = new QueryBatchRequest()
        {
            QueryParams = new FileQueryParamsV1()
            {
                TargetDrive = spec.TargetDrive,
                ArchivalStatus = new List<int>() { archivalStatus }
            },

            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = false
            }
        };

        var response = await caller.V1.Drive.QueryBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SearchResults.Single(item => item.FileMetadata.AppData.ArchivalStatus == archivalStatus), Is.Not.Null);
    }

    [Test]
    public async Task CanQueryDriveModifiedItems()
    {
        var uploadFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0)
            }
        };

        var spec = CallerSpec.SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);
        await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, uploadFileMetadata,
            payloadData: "some payload data for good measure");

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = spec.TargetDrive,
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = true
        };

        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await caller.V1.Drive.QueryBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;
        Assert.That(batch, Is.Not.Null);

        //TODO: what to test here?
        Assert.That(batch!.SearchResults, Is.Not.Empty);
        Assert.That(batch.CursorState, Is.Not.Null);
        Assert.That(batch.CursorState, Is.Not.Empty);

        var firstResult = batch.SearchResults.First();

        //ensure file content was sent
        Assert.That(firstResult.FileMetadata.AppData.Content, Is.Not.Null);
        Assert.That(firstResult.FileMetadata.AppData.Content, Is.Not.Empty);

        Assert.That(firstResult.FileMetadata.AppData.FileType, Is.EqualTo(uploadFileMetadata.AppData.FileType));
        Assert.That(firstResult.FileMetadata.AppData.DataType, Is.EqualTo(uploadFileMetadata.AppData.DataType));
        Assert.That(firstResult.FileMetadata.AppData.UserDate, Is.EqualTo(uploadFileMetadata.AppData.UserDate));
        Assert.That(firstResult.FileMetadata.SenderOdinId, Is.EqualTo(caller.Identity.DomainName));
        Assert.That(firstResult.FileMetadata.OriginalAuthor, Is.EqualTo(caller.Identity));

        //must be ordered correctly
        //TODO: How to test this with a fileId?
    }

    [Test]
    public async Task CanQueryDriveModifiedArchivedItems()
    {
        const int archivalStatus = 1;

        var uploadFileMetadata_not_archived = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                ArchivalStatus = 0
            }
        };

        var notArchivedSpec = CallerSpec.SampleAppWithConnectionReads();
        var notArchivedCaller = await SetupCaller(notArchivedSpec);
        await AppFileUploads.UploadEncryptedAsync(notArchivedCaller, notArchivedSpec.TargetDrive, uploadFileMetadata_not_archived,
            payloadData: "some payload data for good measure");

        var uploadFileMetadata_archived = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                ArchivalStatus = archivalStatus
            }
        };

        var spec = CallerSpec.SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);
        await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, uploadFileMetadata_archived,
            payloadData: "some payload data for good measure");

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = spec.TargetDrive,
            ArchivalStatus = new List<int>() { archivalStatus }
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = true
        };

        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await caller.V1.Drive.QueryBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;
        Assert.That(batch, Is.Not.Null);

        //TODO: what to test here?
        Assert.That(batch!.SearchResults, Is.Not.Empty);
        Assert.That(batch.CursorState, Is.Not.Null);
        Assert.That(batch.CursorState, Is.Not.Empty);

        var theFileResult = batch.SearchResults.Single();

        //ensure file content was sent
        Assert.That(theFileResult.FileMetadata.AppData.Content, Is.Not.Null);
        Assert.That(theFileResult.FileMetadata.AppData.Content, Is.Not.Empty);

        Assert.That(theFileResult.FileMetadata.AppData.FileType, Is.EqualTo(uploadFileMetadata_archived.AppData.FileType));
        Assert.That(theFileResult.FileMetadata.AppData.ArchivalStatus, Is.EqualTo(uploadFileMetadata_archived.AppData.ArchivalStatus));
        Assert.That(theFileResult.FileMetadata.AppData.DataType, Is.EqualTo(uploadFileMetadata_archived.AppData.DataType));
        Assert.That(theFileResult.FileMetadata.AppData.UserDate, Is.EqualTo(uploadFileMetadata_archived.AppData.UserDate));
        Assert.That(theFileResult.FileMetadata.SenderOdinId, Is.EqualTo(caller.Identity.DomainName));
        Assert.That(theFileResult.FileMetadata.OriginalAuthor, Is.EqualTo(caller.Identity));

        //must be ordered correctly
        //TODO: How to test this with a fileId?
    }

    [Test]
    public async Task CanQueryDriveModifiedItemsRedactedContent()
    {
        var uploadFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0)
            }
        };

        var spec = CallerSpec.SampleAppWithConnectionReads();
        var caller = await SetupCaller(spec);
        await AppFileUploads.UploadEncryptedAsync(caller, spec.TargetDrive, uploadFileMetadata,
            payloadData: "some payload data for good measure");

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = spec.TargetDrive,
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "", MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await caller.V1.Drive.QueryBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;
        Assert.That(batch, Is.Not.Null);
        Assert.That(batch!.SearchResults, Is.Not.Empty, "No items returned");
        Assert.That(batch.SearchResults.Where(item => !string.IsNullOrEmpty(item.FileMetadata.AppData.Content)), Is.Empty,
            "One or more items had content");
    }
}
