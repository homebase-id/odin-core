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
/// Port of tests/apps/Odin.Hosting.Tests/AppAPI/Transit/Query/AppTransitQueryTestsForPrivateFiles.cs
///
/// The secured counterpart of <see cref="AppTransitQueryTestsForPublicFiles"/>: Merry and Pippin are
/// connected through the hobbit mesh, which grants read/write on one shared drive, so Merry's
/// transit-reading app can batch, batch-collect, list-modified, and fetch the header, payload and
/// thumbnail of a <see cref="AccessControlList.Connected"/> file on that drive — and sees only the
/// drives of Pippin's it has been granted.
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     <c>_scaffold.Scenarios.CreateConnectedHobbits</c>, at seven call sites here, is
///     <see cref="HobbitScenario.ConnectAllAsync"/> — the mesh connect plus the per-identity app the
///     V1 helper registered. The matching <c>DisconnectHobbits()</c> at the end of each test asserted
///     nothing and is dropped; per-test reset restores that state.
///   </description></item>
///   <item><description>
///     <c>CreateAppAndClient</c> is <see cref="AppTransitClients.CreateAppAsync"/>; the app's
///     <c>TransitQuery</c> client is <see cref="AppTransitClients.QueryFor"/>; the upload and modify
///     helpers are <see cref="AppTransitUploads"/>.
///   </description></item>
///   <item><description>
///     <see cref="AppCan_Get_Secured_Metadata_Type_OverTransitQuery"/> creates its two extra drives
///     from <see cref="TargetDrive"/> values built in the test, because <c>owner.Admin.CreateDrive</c>
///     answers a bool rather than the created drive the V1 client returned. Same drives, same
///     assertions.
///   </description></item>
/// </list>
/// Carried oddity, behaviour left as found: <see cref="AppCan_Get_Secured_Thumbnail_OverTransitQuery"/>
/// compares the thumbnail's <em>byte</em> count against the <em>character</em> count of the response
/// read as a UTF-8 string. It passes because the comparison happens to hold for this fixture's test
/// image, not because it means what it reads as.
/// </remarks>
[TestFixture]
public class AppTransitQueryTestsForPrivateFiles : V2Fixture
{
    /// <remarks>
    /// Issue #1771: a peer upload whose comment encryption disagrees with its referenced file trips
    /// the S2040 guard in <c>PeerFileWriter.GetTargetAcl</c>, is logged at Error, and is retried by the
    /// outbox — the four sibling fixtures in this folder that provoke it already tolerate exactly this
    /// message. It is listed here for a second reason, measured while porting this batch: under
    /// <c>ParallelScope.Fixtures</c> these events reach <em>this</em> fixture's log store even though
    /// none of its tests performs a peer upload. Running this fixture alone is clean over repeated
    /// runs; running it beside <see cref="TransitCommentFileRoutingTests"/> reddens tests here that
    /// make no peer call at all, with that fixture's error text. So the per-host log isolation
    /// <see cref="V2Fixture.AssertNoErrorLogEvents"/> documents does not hold under load. Remove when
    /// #1771 is resolved; the isolation gap is reported separately.
    /// </remarks>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        ["Referenced filed and metadata payload encryption do not match"];

    protected override string[] HostIdentities =>
        [Identities.Merry, Identities.Pippin, Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppCan_Query_Secured_Batch_OverTransitQuery()
    {
        var remoteDrive = TargetDrive.NewTargetDrive();

        //Connected merry and pippin; also grant RW to the remote drive
        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive);

        var thumbnail = new ThumbnailContent
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg",
            Content = TestMedia.ThumbnailBytes300
        };

        // Pippin uploads file
        var randomFile = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive,
            payload: "far and wide", thumbnail: thumbnail);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

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
    public async Task AppCan_Query_Secured_BatchCollection_OverTransitQuery()
    {
        var remoteDrive = TargetDrive.NewTargetDrive();

        //Connected merry and pippin; also grant RW to the remote drive
        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive);

        var thumbnail = new ThumbnailContent
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg",
            Content = TestMedia.ThumbnailBytes300
        };

        const string payloadData = "far and wide";

        // Pippin uploads file
        var randomFile1 = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive, payload: payloadData, thumbnail: thumbnail);
        var randomFile2 = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive, payload: payloadData, thumbnail: thumbnail);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

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
    public async Task AppCan_Query_Secured_Modified_OverTransitQuery()
    {
        var remoteDrive = TargetDrive.NewTargetDrive();

        //Connected merry and pippin; also grant RW to the remote drive
        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive);

        var thumbnail = new ThumbnailContent
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg",
            Content = TestMedia.ThumbnailBytes300
        };

        const string payloadData = "yea, another payload";

        // Pippin uploads file
        var randomFile = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive, payload: payloadData, thumbnail: thumbnail);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

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
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = randomFile.uploadResult.File.TargetDrive,
                FileType = [randomFile.uploadedMetadata.AppData.FileType]
            },
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
    public async Task AppCan_Get_Secured_Header_OverTransitQuery()
    {
        // Prep
        var remoteDrive = TargetDrive.NewTargetDrive();

        //Connected merry and pippin; also grant RW to the remote drive
        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive);

        // Pippin uploads file
        var randomFile = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

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
    public async Task AppCan_Get_Secured_Payload_OverTransitQuery()
    {
        // Prep
        var remoteDrive = TargetDrive.NewTargetDrive();

        //Connected merry and pippin; also grant RW to the remote drive
        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive);

        const string uploadedPayload = "some payload of something secured";

        // Pippin uploads file
        var randomFile = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive, payload: uploadedPayload);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

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
    public async Task AppCan_Get_Secured_Thumbnail_OverTransitQuery()
    {
        // Prep
        var remoteDrive = TargetDrive.NewTargetDrive();

        //Connected merry and pippin; also grant RW to the remote drive
        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive);

        const string payloadData = "far and wide";
        var thumbnail = new ThumbnailContent
        {
            PixelHeight = 300,
            PixelWidth = 300,
            ContentType = "image/jpeg",
            Content = TestMedia.ThumbnailBytes300
        };

        // Pippin uploads file
        var randomFile = await UploadStandardRandomSecureConnectedFile(pippin, remoteDrive, payload: payloadData, thumbnail: thumbnail);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

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
    public async Task AppCan_Get_Secured_Metadata_Type_OverTransitQuery()
    {
        // Connected merry and pippin; also grant RW to the remote drive
        var driveType = Guid.Parse("11111111-2222-3333-a88f-e2475560bced");

        var remoteDrive1GrantedViaCircle = new TargetDrive
        {
            Alias = Guid.NewGuid(),
            Type = driveType
        };

        var (pippin, merry) = await ConnectHobbitsAsync(remoteDrive1GrantedViaCircle);

        var remoteDrive2AnonymousDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };
        var remoteDrive3NeverGrantedToMerry = new TargetDrive { Alias = Guid.NewGuid(), Type = driveType };

        await pippin.Admin.CreateDrive(remoteDrive2AnonymousDrive, "Some target drive allow anonymous=true", allowAnonymousReads: true);
        await pippin.Admin.CreateDrive(remoteDrive3NeverGrantedToMerry, "Some target drive 2", allowAnonymousReads: false);

        var merryApp = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);

        var getTransitDrives = await AppTransitClients.QueryFor(merryApp).GetDrives(new TransitGetDrivesByTypeRequest
        {
            OdinId = pippin.Identity,
            DriveType = driveType,
            PageSize = 10,
            PageNumber = 1
        });

        Assert.That(getTransitDrives.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getTransitDrives.Content, Is.Not.Null);

        var drivesOnRecipientIdentityAccessibleToSender = getTransitDrives.Content!.Results;

        // GuidId defines its own equality operators but NUnit's EqualTo does not see them, so the
        // "every row has this drive type" claim is expressed as "no row has another".
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.Where(d => d.TargetDrive.Type != driveType), Is.Empty);
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive1GrantedViaCircle), Is.Not.Null);
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive2AnonymousDrive), Is.Not.Null);
        Assert.That(drivesOnRecipientIdentityAccessibleToSender.SingleOrDefault(d => d.TargetDrive == remoteDrive3NeverGrantedToMerry), Is.Null);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The hobbit mesh over <paramref name="targetDrive"/>, returning the two identities every test
    /// then acts as. Frodo and Sam take part in the mesh and nothing more — as in the original.
    /// </summary>
    private async Task<(OwnerSession Pippin, OwnerSession Merry)> ConnectHobbitsAsync(TargetDrive targetDrive)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var merry = await LoginAsOwner(Identities.Merry);
        var pippin = await LoginAsOwner(Identities.Pippin);
        var sam = await LoginAsOwner(Identities.Sam);

        await HobbitScenario.ConnectAllAsync([frodo, merry, pippin, sam], targetDrive);
        return (pippin, merry);
    }

    private static Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomSecureConnectedFile(
        OwnerSession owner, TargetDrive targetDrive, string payload = null, ThumbnailContent thumbnail = null) =>
        AppTransitUploads.UploadStandardRandomFileAsync(owner, targetDrive, AccessControlList.Connected, payload, thumbnail);
}
