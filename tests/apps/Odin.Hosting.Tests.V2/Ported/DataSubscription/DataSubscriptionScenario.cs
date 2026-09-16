using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Query;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DataSubscription;

/// <summary>
/// The arrange and the feed-drive assertions the four <c>OwnerApi/DataSubscription</c> fixtures share:
/// a channel drive, the two post shapes they publish to it (unencrypted / encrypted, with or without a
/// payload), and the "is it in the follower's feed" checks.
/// </summary>
/// <remarks>
/// Each V1 original carried its own private copies of these — <c>UploadStandardUnencryptedFileToChannel</c>,
/// <c>UploadStandardEncryptedFileToChannel</c>, <c>OverwriteStandardFile</c>,
/// <c>AssertFeedDriveHasFile</c>, <c>AssertFeedDrive_HasDeletedFile</c> and the four payload assertions —
/// line-for-line identical across the files that had them, so they are parameters here instead.
/// <para>
/// Two carried quirks worth naming, because both look like bugs and neither is changed:
/// <list type="bullet">
/// <item><c>UploadStandardEncryptedFileToChannelAsync</c> sets <c>IsEncrypted = false</c> on the metadata
/// it hands the client. The client overwrites it with <c>true</c> when it encrypts the content, so the
/// upload is genuinely encrypted; the assignment is dead. Carried verbatim.</item>
/// <item>The originals' encrypted-metadata helper returned a three-tuple whose third element was always
/// the empty string, and every call site discarded it with <c>_</c>. Dropped here.</item>
/// </list>
/// </para>
/// </remarks>
internal static class DataSubscriptionScenario
{
    /// <summary>Attributes marking a channel drive as a collaborative ("group") channel.</summary>
    public static readonly Dictionary<string, string> IsGroupChannelAttributes =
        new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };

    /// <summary>A fresh channel drive — <c>Alias = Guid.NewGuid()</c>, <c>Type = ChannelDriveType</c>.</summary>
    public static TargetDrive NewChannelDrive() => new()
    {
        Alias = Guid.NewGuid(),
        Type = SystemDriveConstants.ChannelDriveType
    };

    /// <summary>A feed-drive query filtered on <paramref name="fileType"/>.</summary>
    public static QueryBatchRequest FeedQueryByFileType(int fileType) => FeedQuery(new FileQueryParamsV1
    {
        TargetDrive = WellKnownAppDrives.FeedDrive,
        FileType = [fileType]
    });

    /// <summary>A feed-drive query filtered on one upload's global transit id.</summary>
    public static QueryBatchRequest FeedQueryByGlobalTransitId(UploadResult uploadResult) => FeedQuery(
        new FileQueryParamsV1
        {
            TargetDrive = WellKnownAppDrives.FeedDrive,
            GlobalTransitId = [uploadResult.GlobalTransitId.GetValueOrDefault()]
        });

    /// <summary>
    /// Wraps <paramref name="queryParams"/> in the result options the V1 <c>DriveApiClient.QueryBatch</c>
    /// defaulted to, so a ported query asks for exactly what the original asked for.
    /// </summary>
    public static QueryBatchRequest FeedQuery(FileQueryParamsV1 queryParams) => new()
    {
        QueryParams = queryParams,
        ResultOptionsRequest = new QueryBatchResultOptionsRequest
        {
            CursorState = "",
            MaxRecords = 10,
            IncludeMetadataHeader = true
        }
    };

    /// <summary>Runs a query batch as <paramref name="owner"/> and hands back the search results.</summary>
    public static async Task<List<SharedSecretEncryptedFileHeader>> QueryBatchAsync(
        OwnerSession owner, QueryBatchRequest request, FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var response = await owner.V1.Drive.QueryBatch(request, fileSystemType);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content!.SearchResults.ToList();
    }

    public static async Task<UploadResult> UploadStandardUnencryptedFileToChannelAsync(
        OwnerSession owner, TargetDrive targetDrive, string uploadedContent, int fileType, Guid? uniqueId = null)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                UniqueId = uniqueId,
                Tags = default
            },
            AccessControlList = AccessControlList.Anonymous
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content!;
    }

    public static async Task<(UploadResult UploadResult, string EncryptedJsonContent64)>
        UploadStandardEncryptedFileToChannelAsync(
            OwnerSession owner, TargetDrive targetDrive, string uploadedContent, int fileType, Guid? uniqueId = null)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default,
                UniqueId = uniqueId
            },
            AccessControlList = AccessControlList.Connected
        };

        var (response, encryptedJsonContent64) = await owner.V1.Drive.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (response.Content!, encryptedJsonContent64);
    }

    /// <summary>
    /// One encrypted post carrying a single payload, its ACL narrowed to <paramref name="aclCircleId"/>.
    /// The shape the two <c>…Tests2</c> fixtures publish.
    /// </summary>
    public static async Task<(UploadResult UploadResult, string EncryptedJsonContent64, string EncryptedPayloadContent64)>
        UploadStandardEncryptedFileWithPayloadToChannelAsync(
            OwnerSession owner, TargetDrive targetDrive, string headerContent, string payloadContent, Guid aclCircleId)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = headerContent,
                GroupId = default,
                Tags = default
            },
            AccessControlList = new AccessControlList
            {
                CircleIdList = [aclCircleId],
                RequiredSecurityGroup = SecurityGroupType.Connected
            }
        };

        var testPayloads = new List<TestPayloadDefinition>
        {
            new()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                Key = WebScaffold.PAYLOAD_KEY,
                ContentType = "text/plain",
                Content = payloadContent.ToUtf8ByteArray(),
                Thumbnails = []
            }
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var (response, encryptedJsonContent64, _, uploadedPayloads) = await owner.V1.Drive.UploadNewEncryptedFile(
            targetDrive, KeyHeader.NewRandom16(), fileMetadata, uploadManifest, testPayloads);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (response.Content!, encryptedJsonContent64, uploadedPayloads.First().EncryptedContent64);
    }

    /// <summary>Edits a post in place, as if its author changed the text.</summary>
    public static async Task<UploadResult> OverwriteStandardFileAsync(
        OwnerSession owner, ExternalFileIdentifier overwriteFile, string uploadedContent, int fileType, Guid versionTag)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            VersionTag = versionTag,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Anonymous
        };

        var response = await owner.V1.Drive.UpdateExistingMetadata(overwriteFile, versionTag, fileMetadata);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content!;
    }

    /// <summary>The follower's feed holds exactly one matching file, active, with the expected content.</summary>
    public static async Task AssertFeedDriveHasFileAsync(
        OwnerSession follower, QueryBatchRequest request, string expectedContent, UploadResult expectedUploadResult)
    {
        var searchResults = await QueryBatchAsync(follower, request);
        Assert.That(searchResults.Count, Is.EqualTo(1));

        var theFile = searchResults.First();
        Assert.That(theFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(theFile.FileMetadata.AppData.Content, Is.EqualTo(expectedContent));
        Assert.That(theFile.FileMetadata.GlobalTransitId, Is.EqualTo(expectedUploadResult.GlobalTransitId));
    }

    /// <summary>The follower's feed holds the file, marked deleted.</summary>
    public static async Task AssertFeedDriveHasDeletedFileAsync(OwnerSession follower, UploadResult uploadResult)
    {
        var searchResults = await QueryBatchAsync(follower, FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(searchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(
            c => c.FileState == FileState.Deleted));
    }

    /// <summary>Nothing in the follower's feed carries that global transit id.</summary>
    public static async Task AssertFeedDriveDoesNotHaveHeaderAsync(OwnerSession follower, UploadResult uploadResult)
    {
        var searchResults = await QueryBatchAsync(follower, FeedQueryByGlobalTransitId(uploadResult));
        Assert.That(searchResults, Is.Empty);
    }

    // ---------------------------------------------------------------------------------------------
    // Peer payload reads. The originals went through OwnerApiClient.TransitQuery; the same endpoint is
    // reached here through IUniversalRefitPeerQuery, which the README sanctions over hand-rolling the
    // UniversalPeerQueryApiClient wrapper (see Ported/DriveQuery/PeerQueryTests for the same call).
    // ---------------------------------------------------------------------------------------------

    /// <summary>The reader can pull the post's payload from <paramref name="host"/> over peer query.</summary>
    public static async Task AssertCanGetPayloadAsync(
        OwnerSession reader, OwnerSession host, UploadResult uploadResult, string encryptedPayloadContent64)
    {
        var payloadResponse = await GetPayloadOverPeerAsync(reader, host, uploadResult);

        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(payloadResponse.Content, Is.Not.Null, "payload content is null");
        var bytes = await payloadResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(bytes.Length, Is.GreaterThan(0));
        Assert.That(bytes.ToBase64(), Is.EqualTo(encryptedPayloadContent64));
    }

    /// <summary>The reader is refused the payload.</summary>
    public static async Task AssertCanNotGetPayloadAsync(OwnerSession reader, OwnerSession host, UploadResult uploadResult)
    {
        var payloadResponse = await GetPayloadOverPeerAsync(reader, host, uploadResult);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>The payload is gone.</summary>
    public static async Task AssertPayloadIs404Async(OwnerSession reader, OwnerSession host, UploadResult uploadResult)
    {
        var payloadResponse = await GetPayloadOverPeerAsync(reader, host, uploadResult);
        Assert.That(payloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static Task<Refit.ApiResponse<System.Net.Http.HttpContent>> GetPayloadOverPeerAsync(
        OwnerSession reader, OwnerSession host, UploadResult uploadResult) =>
        reader.RefitFor<IUniversalRefitPeerQuery>().GetPayload(new TransitGetPayloadRequest
        {
            OdinId = host.Identity,
            File = uploadResult.File,
            Key = WebScaffold.PAYLOAD_KEY
        });
}
