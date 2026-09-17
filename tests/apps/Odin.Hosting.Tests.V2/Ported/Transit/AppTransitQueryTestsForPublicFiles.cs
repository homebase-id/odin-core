using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/AppAPI/Transit/Query/AppTransitQueryTestsForPublicFiles.cs
///
/// Merry's app holds <see cref="PermissionKeys.UseTransitRead"/> but no connection to Pippin. Every
/// transit read — batch, batch collection, modified, header, payload, thumbnail, drives-by-type —
/// therefore sees exactly Pippin's anonymous drives and the anonymous files on them.
/// </summary>
/// <remarks>
/// Port notes: the private <c>CreateAppAndClient</c> is <see cref="AppTransitClients.CreateAppAsync"/>,
/// the app's <c>TransitQuery</c> client is <see cref="AppTransitClients.QueryFor"/>, and the two
/// upload helpers are <see cref="AppTransitUploads"/> — see those classes for the one inert wire-level
/// difference each carries. The <c>Task.Delay(5)</c> before <c>GetModified</c> is kept verbatim: the
/// modified-file cursor is time-based, so it is load-bearing here in a way it is not in the
/// permission fixture.
/// </remarks>
[TestFixture]
public class AppTransitQueryTestsForPublicFiles : V2Fixture
{

    protected override string[] HostIdentities => [Identities.Pippin, Identities.Merry];

    [Test]
    public async Task AppCan_Query_Public_Batch_OverTransitQuery()
    {
        // Prep
        var (pippin, merryApp, remoteDrive) = await PrepareAsync();

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        //
        // Merry uses transit query to get all files of that file type
        //
        var request = new PeerQueryBatchRequest
        {
            OdinId = pippin.Identity,
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = randomFile.uploadResult.File.TargetDrive,
                ClientUniqueIdAtLeastOne = [randomFile.uploadedMetadata.AppData.UniqueId.GetValueOrDefault()]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                IncludeMetadataHeader = true,
                MaxRecords = 10,
                Ordering = QueryBatchSortOrder.NewestFirst,
                Sorting = QueryBatchSortField.CreatedDate
            }
        };

        var getBatchResponse = await AppTransitClients.QueryFor(merryApp).GetBatch(request);
        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getBatchResponse.Content, Is.Not.Null);
        Assert.That(getBatchResponse.Content!.SearchResults,
            Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(sr => sr.FileId == randomFile.uploadResult.File.FileId));
    }

    [Test]
    public async Task AppCan_Query_Public_BatchCollection_OverTransitQuery()
    {
        // Prep
        var (pippin, merryApp, remoteDrive) = await PrepareAsync();

        // Pippin uploads file
        var randomFile1 = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);
        var randomFile2 = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        const string testResult1 = "test01";
        const string testResult2 = "test02";
        var request = new PeerQueryBatchCollectionRequest
        {
            OdinId = pippin.Identity,
            Queries = new List<CollectionQueryParamSection>
            {
                new()
                {
                    Name = testResult1,
                    QueryParams = new FileQueryParamsV1
                    {
                        TargetDrive = randomFile1.uploadResult.File.TargetDrive,
                        ClientUniqueIdAtLeastOne = [randomFile1.uploadedMetadata.AppData.UniqueId.GetValueOrDefault()]
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest
                    {
                        MaxRecords = 100,
                        IncludeMetadataHeader = true
                    }
                },
                new()
                {
                    Name = testResult2,
                    QueryParams = new FileQueryParamsV1
                    {
                        TargetDrive = randomFile2.uploadResult.File.TargetDrive,
                        ClientUniqueIdAtLeastOne = [randomFile2.uploadedMetadata.AppData.UniqueId.GetValueOrDefault()]
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest
                    {
                        MaxRecords = 100,
                        IncludeMetadataHeader = true
                    }
                }
            }
        };

        var collectionResponse = await AppTransitClients.QueryFor(merryApp).GetBatchCollection(request);

        Assert.That(collectionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(collectionResponse.Content, Is.Not.Null);
        Assert.That(collectionResponse.Content!.Results.Count, Is.EqualTo(2));

        var set1 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult1.ToLower());
        Assert.That(set1, Is.Not.Null);

        var set1File1 = set1!.SearchResults.SingleOrDefault(f => f.FileId == randomFile1.uploadResult.File.FileId);
        Assert.That(set1File1, Is.Not.Null);

        var set2 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult2.ToLower());
        Assert.That(set2, Is.Not.Null);

        var set2File1 = set2!.SearchResults.SingleOrDefault(f => f.FileId == randomFile2.uploadResult.File.FileId);
        Assert.That(set2File1, Is.Not.Null);
    }

    [Test]
    public async Task AppCan_Query_Public_Modified_OverTransitQuery()
    {
        // Prep
        var (pippin, merryApp, remoteDrive) = await PrepareAsync();

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        // Pippin now modifies that file
        var modifiedResult = await AppTransitUploads.ModifyFileAsync(pippin, randomFile.uploadResult.File);
        Assert.That(modifiedResult.uploadResult.File, Is.EqualTo(randomFile.uploadResult.File));
        Assert.That(modifiedResult.modifiedMetadata.AppData.Content,
            Is.Not.EqualTo(randomFile.uploadedMetadata.AppData.Content), "file was not modified");

        //
        // Merry uses transit query to get modified files (deleted files show up as modified)
        //
        var request = new PeerQueryModifiedRequest
        {
            OdinId = pippin.Identity,
            QueryParams = FileQueryParamsV1.FromFileType(randomFile.uploadResult.File.TargetDrive,
                randomFile.uploadedMetadata.AppData.FileType),
            ResultOptions = new QueryModifiedResultOptions
            {
                IncludeHeaderContent = true,
                MaxRecords = 100
            }
        };

        await Task.Delay(5);

        var getBatchResponse = await AppTransitClients.QueryFor(merryApp).GetModified(request);
        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getBatchResponse.Content, Is.Not.Null);
        var theModifiedFile = getBatchResponse.Content!.SearchResults.SingleOrDefault(sr => sr.FileId == randomFile.uploadResult.File.FileId);
        Assert.That(theModifiedFile, Is.Not.Null);
        Assert.That(theModifiedFile!.FileMetadata.AppData.Content, Is.EqualTo(modifiedResult.modifiedMetadata.AppData.Content));
    }

    [Test]
    public async Task AppCan_Get_Public_Header_OverTransitQuery()
    {
        // Prep
        var (pippin, merryApp, remoteDrive) = await PrepareAsync();

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive);

        var response = await AppTransitClients.QueryFor(merryApp).GetFileHeader(new TransitExternalFileIdentifier
        {
            OdinId = pippin.Identity,
            File = randomFile.uploadResult.File
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        Assert.That(response.Content!.FileMetadata.AppData.Content, Is.EqualTo(randomFile.uploadedMetadata.AppData.Content));
    }

    [Test]
    public async Task AppCan_Get_Public_Payload_OverTransitQuery()
    {
        // Prep
        var (pippin, merryApp, remoteDrive) = await PrepareAsync();

        const string uploadedPayload = "some payload of something";

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive, uploadedPayload);

        var response = await AppTransitClients.QueryFor(merryApp).GetPayload(new TransitGetPayloadRequest
        {
            OdinId = pippin.Identity,
            File = randomFile.uploadResult.File,
            Key = WebScaffold.PAYLOAD_KEY
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        var payload = await response.Content!.ReadAsStringAsync();
        Assert.That(payload, Is.EqualTo(uploadedPayload));
    }

    [Test]
    public async Task AppCan_Get_Public_Thumbnail_OverTransitQuery()
    {
        // Prep
        var (pippin, merryApp, remoteDrive) = await PrepareAsync();

        var thumbnail = new ThumbnailContent
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg",
            Content = TestMedia.ThumbnailBytes300
        };

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(pippin, remoteDrive,
            payload: "le payload", thumbnail: thumbnail);

        var response = await AppTransitClients.QueryFor(merryApp).GetThumbnail(new TransitGetThumbRequest
        {
            OdinId = pippin.Identity,
            File = randomFile.uploadResult.File,
            Width = thumbnail.PixelWidth,
            Height = thumbnail.PixelHeight,
            PayloadKey = WebScaffold.PAYLOAD_KEY,
            DirectMatchOnly = true
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        var thumbnailContent = await response.Content!.ReadAsStringAsync();
        Assert.That(thumbnailContent.Length, Is.EqualTo(thumbnail.Content.Length));
    }

    [Test]
    public async Task AppCan_Get_Public_Drives_By_Type_OverTransitQuery()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);

        var driveType = Guid.NewGuid();
        var remoteDrive1 = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };
        var remoteDrive2 = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };
        var remoteDrive3 = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };

        await pippin.Admin.CreateDrive(remoteDrive1, "Some target drive 1", allowAnonymousReads: true);
        await pippin.Admin.CreateDrive(remoteDrive2, "Some target drive 2", allowAnonymousReads: true);
        await pippin.Admin.CreateDrive(remoteDrive3, "Some target drive 3 - no anonymous reads", allowAnonymousReads: false);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

        var getTransitDrives = await AppTransitClients.QueryFor(merryApp).GetDrives(new TransitGetDrivesByTypeRequest
        {
            OdinId = pippin.Identity,
            DriveType = driveType
        });

        Assert.That(getTransitDrives.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getTransitDrives.Content, Is.Not.Null);

        var drivesOnRecipientIdentityAccessibleToSender = getTransitDrives.Content!.Results;

        // GuidId defines its own equality operators but NUnit's EqualTo does not see them, so the
        // "every row has this drive type" claim is expressed as "no row has another".
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.Where(d => d.TargetDrive.Type != driveType), Is.Empty);
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.Count, Is.EqualTo(2));
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive1), Is.Not.Null);
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive2), Is.Not.Null);
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive3), Is.Null);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The arrange six of the seven tests share: Pippin's anonymous drive and Merry's transit-reading
    /// app.
    /// </summary>
    private async Task<(OwnerSession Pippin, AppSession MerryApp, TargetDrive RemoteDrive)> PrepareAsync()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);

        var remoteDrive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(remoteDrive, "Some target drive", allowAnonymousReads: true);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);
        return (pippin, merryApp, remoteDrive);
    }

    private static Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomPublicFileHeader(
        OwnerSession owner, TargetDrive targetDrive, string payload = null, ThumbnailContent thumbnail = null) =>
        AppTransitUploads.UploadStandardRandomFileAsync(owner, targetDrive, AccessControlList.Anonymous, payload, thumbnail);
}
