using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Abstractions;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Query;
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
/// the app's <c>TransitQuery</c> client is <c>app.RefitFor&lt;IRefitAppTransitQuery&gt;()</c>, and the
/// two upload helpers are <see cref="AppTransitUploads"/> — see those classes for the one inert
/// wire-level difference each carries. The <c>Task.Delay(5)</c> before <c>GetModified</c> is kept
/// verbatim: the modified-file cursor is time-based, so it is load-bearing here in a way it is not in
/// the permission fixture.
/// <para>
/// The arrange every test shares — Pippin's anonymous drive and Merry's transit-reading app — is baked
/// into the baseline snapshot by <see cref="WarmTenantBaselineAsync"/> rather than rebuilt per test:
/// drives and app registrations are restored by the per-test reset, so one registration serves all
/// seven. Uploads stay in the tests, where they must be — the payload tree is wiped between tests.
/// </para>
/// </remarks>
[TestFixture]
public class AppTransitQueryTestsForPublicFiles : V2Fixture
{
    private OwnerSession _pippin;
    private AppSession _merryApp;
    private TargetDrive _remoteDrive;

    protected override string[] HostIdentities => [Identities.Pippin, Identities.Merry];

    /// <summary>
    /// Baked into the baseline: Pippin's anonymous drive, and Merry's app holding
    /// <see cref="PermissionKeys.UseTransitRead"/> over a drive of its own. Both survive the per-test
    /// DB restore, as does the app client's token, which is issued here and snapshotted with it.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        _pippin = await LoginAsOwner(Identities.Pippin);
        var merry = await LoginAsOwner(Identities.Merry);

        _remoteDrive = TargetDrive.NewTargetDrive();
        await _pippin.Admin.CreateDrive(_remoteDrive, "Some target drive", allowAnonymousReads: true);

        _merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);
    }

    [Test]
    public async Task AppCan_Query_Public_Batch_OverTransitQuery()
    {
        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive);

        //
        // Merry uses transit query to get all files of that file type
        //
        var request = new PeerQueryBatchRequest
        {
            OdinId = _pippin.Identity,
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

        var getBatchResponse = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetBatch(request);
        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getBatchResponse.Content, Is.Not.Null);
        Assert.That(getBatchResponse.Content!.SearchResults,
            Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(sr => sr.FileId == randomFile.uploadResult.File.FileId));
    }

    [Test]
    public async Task AppCan_Query_Public_BatchCollection_OverTransitQuery()
    {
        // Pippin uploads file
        var randomFile1 = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive);
        var randomFile2 = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive);

        const string testResult1 = "test01";
        const string testResult2 = "test02";
        var request = new PeerQueryBatchCollectionRequest
        {
            OdinId = _pippin.Identity,
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

        var collectionResponse = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetBatchCollection(request);

        Assert.That(collectionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(collectionResponse.Content, Is.Not.Null);
        Assert.That(collectionResponse.Content!.Results.Count, Is.EqualTo(2));

        var set1 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult1.ToLower());
        Assert.That(set1, Is.Not.Null);
        Assert.That(set1!.SearchResults,
            Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(f => f.FileId == randomFile1.uploadResult.File.FileId));

        var set2 = collectionResponse.Content.Results.SingleOrDefault(r => r.Name.ToLower() == testResult2.ToLower());
        Assert.That(set2, Is.Not.Null);
        Assert.That(set2!.SearchResults,
            Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(f => f.FileId == randomFile2.uploadResult.File.FileId));
    }

    [Test]
    public async Task AppCan_Query_Public_Modified_OverTransitQuery()
    {
        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive);

        // Pippin now modifies that file
        var modifiedResult = await AppTransitUploads.ModifyFileAsync(_pippin, randomFile.uploadResult.File);
        Assert.That(modifiedResult.uploadResult.File, Is.EqualTo(randomFile.uploadResult.File));
        Assert.That(modifiedResult.modifiedMetadata.AppData.Content,
            Is.Not.EqualTo(randomFile.uploadedMetadata.AppData.Content), "file was not modified");

        //
        // Merry uses transit query to get modified files (deleted files show up as modified)
        //
        var request = new PeerQueryModifiedRequest
        {
            OdinId = _pippin.Identity,
            QueryParams = FileQueryParamsV1.FromFileType(randomFile.uploadResult.File.TargetDrive,
                randomFile.uploadedMetadata.AppData.FileType),
            ResultOptions = new QueryModifiedResultOptions
            {
                IncludeHeaderContent = true,
                MaxRecords = 100
            }
        };

        await Task.Delay(5);

        var getBatchResponse = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetModified(request);
        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getBatchResponse.Content, Is.Not.Null);
        var theModifiedFile = getBatchResponse.Content!.SearchResults.SingleOrDefault(sr => sr.FileId == randomFile.uploadResult.File.FileId);
        Assert.That(theModifiedFile, Is.Not.Null);
        Assert.That(theModifiedFile!.FileMetadata.AppData.Content, Is.EqualTo(modifiedResult.modifiedMetadata.AppData.Content));
    }

    [Test]
    public async Task AppCan_Get_Public_Header_OverTransitQuery()
    {
        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive);

        var response = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetFileHeader(new TransitExternalFileIdentifier
        {
            OdinId = _pippin.Identity,
            File = randomFile.uploadResult.File
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        Assert.That(response.Content!.FileMetadata.AppData.Content, Is.EqualTo(randomFile.uploadedMetadata.AppData.Content));
    }

    [Test]
    public async Task AppCan_Get_Public_Payload_OverTransitQuery()
    {
        const string uploadedPayload = "some payload of something";

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive, uploadedPayload);

        var response = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetPayload(new TransitGetPayloadRequest
        {
            OdinId = _pippin.Identity,
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
        var thumbnail = new ThumbnailContent
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg",
            Content = TestMedia.ThumbnailBytes300
        };

        // Pippin uploads file
        var randomFile = await UploadStandardRandomPublicFileHeader(_pippin, _remoteDrive,
            payload: "le payload", thumbnail: thumbnail);

        var response = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetThumbnail(new TransitGetThumbRequest
        {
            OdinId = _pippin.Identity,
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
        // A drive type minted here, so the count assertion below sees only these three drives and not
        // the baseline's anonymous one.
        var driveType = Guid.NewGuid();
        var remoteDrive1 = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };
        var remoteDrive2 = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };
        var remoteDrive3 = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };

        await _pippin.Admin.CreateDrive(remoteDrive1, "Some target drive 1", allowAnonymousReads: true);
        await _pippin.Admin.CreateDrive(remoteDrive2, "Some target drive 2", allowAnonymousReads: true);
        await _pippin.Admin.CreateDrive(remoteDrive3, "Some target drive 3 - no anonymous reads", allowAnonymousReads: false);

        var getTransitDrives = await _merryApp.RefitFor<IRefitAppTransitQuery>().GetDrives(new TransitGetDrivesByTypeRequest
        {
            OdinId = _pippin.Identity,
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

    private static Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomPublicFileHeader(
        OwnerSession owner, TargetDrive targetDrive, string payload = null, ThumbnailContent thumbnail = null) =>
        AppTransitUploads.UploadStandardRandomFileAsync(owner, targetDrive, AccessControlList.Anonymous, payload, thumbnail);
}
