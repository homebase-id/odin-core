using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

/// <summary>
/// Port of <c>YouAuthApi/Drive/DriveQueryTests</c>. What an anonymous (no-token) caller sees when it
/// queries an anonymous-readable drive: the public files, and never the secured ones sharing it.
/// </summary>
/// <remarks>
/// The caller is the no-credential one — <c>WebScaffold.CreateAnonymousApiHttpClient</c> in the
/// original, <c>Host.AnonymousRefitFor<T>()</c> here. <c>GuestSession</c> is the wrong tool: it always
/// carries a YouAuth token, which is a different caller from the one these tests are about.
/// <para>
/// The original reached the owner through <c>TestIdentities.InitializedIdentities[identity]</c>,
/// which is null under <c>V2Fixture</c> (only <c>WebScaffold.RunBeforeAnyTests</c> populates it).
/// The owner session is passed down instead. It pinned <c>TestIdentities.Samwise</c>; nothing here
/// reads the identity except <see cref="CanQueryDriveModifiedItems"/>, which compares it against the
/// file's sender/author, so the fixture default serves.
/// </para>
/// <para>
/// No <c>SetupCallerWithOwner</c> (no caller matrix), so its create-drive/build-caller ordering
/// caveat does not apply.
/// </para>
/// <para>
/// Carried defect: <see cref="CanQueryDriveModifiedItems"/> and
/// <see cref="CanQueryDriveModifiedItemsRedactedContent"/> are named for the modified-items endpoint
/// but both call <c>query/batch</c>. Left as found — see the porting rule on carried defects.
/// </para>
/// </remarks>
[TestFixture]
public class DriveQueryTests : V2Fixture
{
    [Test]
    public async Task ShouldNotReturnSecuredFile_QueryBatch()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();

        var targetDrive = TargetDrive.NewTargetDrive();

        //note: must allow anonymous so youauth can read it
        await owner.Admin.CreateDrive(targetDrive, "test drive", allowAnonymousReads: true);
        await UploadFile2(owner, targetDrive, null, tag, AccessControlList.Connected);
        var (anonymousFileUploadResult, _) = await UploadFile2(owner, targetDrive, null, tag, AccessControlList.Anonymous);

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);
        var qp = new FileQueryParamsV1()
        {
            TargetDrive = targetDrive,
            TagsMatchAtLeastOne = new List<Guid>() { tag }
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await svc.GetBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;

        Assert.That(batch, Is.Not.Null);
        //should only be the anonymous file we uploaded
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        Assert.That(batch.SearchResults.Single().FileId, Is.EqualTo(anonymousFileUploadResult.File.FileId));
    }

    [Test]
    public async Task ShouldNotReturnSecuredFile_QueryBatch_WhenSecuredWithCircle()
    {
        var owner = await LoginAsOwner();

        var tag = Guid.NewGuid();
        var targetDrive = TargetDrive.NewTargetDrive();

        //note: must allow anonymous so youauth can read it
        await owner.Admin.CreateDrive(targetDrive, "test drive", allowAnonymousReads: true);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Security Circle",
            TestUtils.CreatePermissionGrantRequest(targetDrive, DrivePermission.None));

        var circleSecuredAcl = new AccessControlList()
        {
            CircleIdList = new List<Guid>() { circleId },
            RequiredSecurityGroup = SecurityGroupType.Connected
        };

        await UploadFile2(owner, targetDrive, null, tag, AccessControlList.Connected);
        var (anonymousFileUploadResult, _) = await UploadFile2(owner, targetDrive, null, tag, AccessControlList.Anonymous);
        await UploadFile2(owner, targetDrive, null, tag, circleSecuredAcl);

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = targetDrive,
            TagsMatchAtLeastOne = new List<Guid>() { tag }
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);
        var response = await svc.GetBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;

        Assert.That(batch, Is.Not.Null);
        //should only be the anonymous file we uploaded
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        Assert.That(batch.SearchResults.Single().FileId, Is.EqualTo(anonymousFileUploadResult.File.FileId));
    }

    [Test]
    public async Task ShouldNotReturnSecuredFile_QueryModified()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();
        var targetDrive = TargetDrive.NewTargetDrive();

        //note: must allow anonymous so youauth can read it
        await owner.Admin.CreateDrive(targetDrive, "test drive", allowAnonymousReads: true);

        var securedFileUploadContext = await UploadFile2(owner, targetDrive, null, tag, AccessControlList.Connected);
        var anonymousFileUploadContext = await UploadFile2(owner, targetDrive, null, tag, AccessControlList.Anonymous);

        //overwrite them to ensure the updated timestamp is set
        await UploadFile2(owner, targetDrive,
            overwriteFileId: securedFileUploadContext.uploadResult.File.FileId,
            tag,
            AccessControlList.Connected, versionTag: securedFileUploadContext.uploadResult.NewVersionTag);

        anonymousFileUploadContext = await UploadFile2(owner, targetDrive,
            overwriteFileId: anonymousFileUploadContext.uploadResult.File.FileId,
            tag,
            AccessControlList.Anonymous, versionTag: anonymousFileUploadContext.uploadResult.NewVersionTag);

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = targetDrive,
            TagsMatchAtLeastOne = new List<Guid>() { tag }
        };

        var resultOptions = new QueryModifiedResultOptions()
        {
            MaxDate = UnixTimeUtc.Now().AddHours(+1).milliseconds,
            MaxRecords = 10,
            IncludeHeaderContent = false
        };

        var request = new QueryModifiedRequest()
        {
            QueryParams = qp,
            ResultOptions = resultOptions
        };

        await Task.Delay(5);

        var getModifiedResponse = await svc.GetModified(request);
        Assert.That(getModifiedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = getModifiedResponse.Content;

        Assert.That(batch, Is.Not.Null);
        //should only be the anonymous file we uploaded
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        Assert.That(batch.SearchResults.Single().FileId,
            Is.EqualTo(anonymousFileUploadContext.uploadResult.File.FileId));
    }

    [Test]
    public async Task CanQueryBatchByOneTag()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();
        var (uploadResult, _) = await UploadFile(owner, tag, SecurityGroupType.Anonymous);

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = uploadResult.File.TargetDrive,
            TagsMatchAtLeastOne = new List<Guid>() { tag }
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);
        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await svc.GetBatch(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;

        Assert.That(batch, Is.Not.Null);
        Assert.That(batch.SearchResults.Count(item => item.FileMetadata.AppData.Tags.Any(t => t == tag)),
            Is.EqualTo(1));
    }

    [Test]
    public async Task CanQueryDriveModifiedItems()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();
        var (uploadResult, uploadFileMetadata) = await UploadFile(owner, tag, SecurityGroupType.Anonymous);

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = uploadResult.File.TargetDrive,
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = true
        };

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);
        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await svc.GetBatch(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;
        Assert.That(batch, Is.Not.Null);

        //TODO: what to test here?
        Assert.That(batch.SearchResults, Is.Not.Empty);
        Assert.That(batch.CursorState, Is.Not.Null);
        Assert.That(batch.CursorState, Is.Not.Empty);

        var firstResult = batch.SearchResults.First();

        //ensure file content was sent
        Assert.That(firstResult.FileMetadata.AppData.Content, Is.Not.Null);
        Assert.That(firstResult.FileMetadata.AppData.Content, Is.Not.Empty);

        Assert.That(firstResult.FileMetadata.AppData.FileType, Is.EqualTo(uploadFileMetadata.AppData.FileType));
        Assert.That(firstResult.FileMetadata.AppData.DataType, Is.EqualTo(uploadFileMetadata.AppData.DataType));
        Assert.That(firstResult.FileMetadata.AppData.UserDate, Is.EqualTo(uploadFileMetadata.AppData.UserDate));
        Assert.That(firstResult.FileMetadata.SenderOdinId, Is.EqualTo(owner.Identity.DomainName));
        Assert.That(firstResult.FileMetadata.OriginalAuthor, Is.EqualTo(owner.Identity));

        //must be ordered correctly
        //TODO: How to test this with a fileId?
    }

    [Test]
    public async Task CanQueryDriveModifiedItemsRedactedContent()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();
        var (uploadResult, _) = await UploadFile(owner, tag, SecurityGroupType.Anonymous);

        var qp = new FileQueryParamsV1()
        {
            TargetDrive = uploadResult.File.TargetDrive,
        };

        var resultOptions = new QueryBatchResultOptionsRequest()
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = false
        };

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);
        var request = new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = resultOptions
        };

        var response = await svc.GetBatch(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var batch = response.Content;
        Assert.That(batch, Is.Not.Null);
        Assert.That(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.Content)),
            Is.True, "One or more items had content");
    }

    /// <summary>Creates a fresh anonymous-readable drive and puts one file on it.</summary>
    private static async Task<(UploadResult uploadResult, UploadFileMetadata uploadedFileMetadata)> UploadFile(
        OwnerSession owner, Guid tag, SecurityGroupType requiredSecurityGroup)
    {
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
            },
            AccessControlList = new AccessControlList()
            {
                RequiredSecurityGroup = requiredSecurityGroup
            }
        };

        var td = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(td, "a drive", allowAnonymousReads: true);
        var response = await owner.V1.Drive.UploadNewMetadata(td, uploadFileMetadata);
        return (response.Content, uploadFileMetadata);
    }

    /// <summary>Puts a file on an existing drive, or overwrites one that is already there.</summary>
    private static async Task<(UploadResult uploadResult, UploadFileMetadata uploadedFileMetadata)> UploadFile2(
        OwnerSession owner,
        TargetDrive drive,
        Guid? overwriteFileId,
        Guid tag,
        AccessControlList acl,
        Guid? versionTag = null)
    {
        var uploadFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            VersionTag = versionTag,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = new List<Guid>() { tag }
            },
            AccessControlList = acl
        };

        if (overwriteFileId.HasValue)
        {
            var targetFile = new ExternalFileIdentifier()
            {
                TargetDrive = drive,
                FileId = overwriteFileId.GetValueOrDefault()
            };

            var response = await owner.V1.Drive.UpdateExistingMetadata(targetFile,
                versionTag.GetValueOrDefault(), uploadFileMetadata);
            return (response.Content, uploadFileMetadata);
        }

        var uploadResponse = await owner.V1.Drive.UploadNewMetadata(drive, uploadFileMetadata);
        return (uploadResponse.Content, uploadFileMetadata);
    }
}
