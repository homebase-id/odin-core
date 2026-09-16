using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Apps;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Ported.DriveQuery;

/// <summary>
/// Port of <c>OwnerApi/Drive/Misc/MultipleQueryConsumersTest</c>. One tenant uses both the standard
/// and the comment query consumer against the same drive in the same test: a standard file and a
/// comment file are uploaded, then each is found through its own file system's query batch.
/// </summary>
/// <remarks>
/// No caller matrix — an <c>OwnerApi</c> fixture, so a plain <c>[Test]</c> and <c>LoginAsOwner</c>;
/// the <c>SetupCallerWithOwner</c> ordering caveat does not apply. Drive creation moves to
/// <c>owner.Admin.CreateDrive</c>, whose <c>Metadata</c> default (<c>string.Empty</c>) matches the
/// original's <c>""</c> and which no assertion reads.
///
/// <b>Coverage note — the log-event net is gone.</b> The original's name ("WillDispose…") pointed at
/// its <c>[TearDown] _scaffold.AssertLogEvents()</c>, which fails a test that logged any
/// Error/Fatal event. The fast framework captures no log events, so that net does not survive the
/// port. What does survive is the failure mode itself: if the two query consumers collided on a
/// disposed connection again, the second <c>QueryBatch</c> would not return its file and the
/// assertions below would fail. Recorded here rather than only in the commit message.
/// </remarks>
[TestFixture]
public class MultipleQueryConsumersTest : V2Fixture
{
    [Test]
    public async Task WillDisposeWithBothCommentAndStandardFilesAreUsed()
    {
        var frodoOwnerClient = await LoginAsOwner();

        var targetDrive = TargetDrive.NewTargetDrive();
        await frodoOwnerClient.Admin.CreateDrive(targetDrive, "Some drive", allowAnonymousReads: false, ownerOnly: true);

        var standardFile = new UploadFileMetadata()
        {
            IsEncrypted = false,
            AllowDistribution = true,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 101,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        var standardUploadResponse =
            await frodoOwnerClient.V1.Drive.UploadNewMetadata(targetDrive, standardFile, FileSystemType.Standard);
        Assert.That(standardUploadResponse.IsSuccessStatusCode, Is.True);
        var standardFileUploadResult = standardUploadResponse.Content!;

        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        var commentManifest = new UploadManifest()
        {
            PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
            {
                new()
                {
                    Iv = null,
                    PayloadKey = WebScaffold.PAYLOAD_KEY,
                    Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                }
            }
        };

        var commentPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Iv = null,
                Key = WebScaffold.PAYLOAD_KEY,
                ContentType = "application/x-binary",
                Content = "some payload data".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
            }
        };

        var commentUploadResponse = await frodoOwnerClient.V1.Drive.UploadNewFile(targetDrive, commentFile, commentManifest,
            commentPayloads, fileSystemType: FileSystemType.Comment);
        Assert.That(commentUploadResponse.IsSuccessStatusCode, Is.True);
        var commentFileUploadResult = commentUploadResponse.Content!;

        var standardFileResults = await QueryBatch(frodoOwnerClient, FileSystemType.Standard, new FileQueryParamsV1()
        {
            TargetDrive = targetDrive,
            FileType = new[] { standardFile.AppData.FileType }
        });

        Assert.That(standardFileResults.SearchResults,
            Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(f => f.FileId == standardFileUploadResult.File.FileId));

        var commentFileResults = await QueryBatch(frodoOwnerClient, FileSystemType.Comment, new FileQueryParamsV1()
        {
            TargetDrive = targetDrive,
            FileType = new[] { commentFile.AppData.FileType }
        });

        Assert.That(commentFileResults.SearchResults,
            Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(f => f.FileId == commentFileUploadResult.File.FileId));
    }

    /// <summary>
    /// The result options the original's <c>DriveApiClient.QueryBatch</c> defaulted to, kept as-is.
    /// </summary>
    private static async Task<QueryBatchResponse> QueryBatch(OwnerSession owner, FileSystemType fileSystemType,
        FileQueryParamsV1 qp)
    {
        var response = await owner.V1.Drive.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = qp,
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        }, fileSystemType);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);
        return response.Content!;
    }
}
