using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A new file whose uniqueId is taken is refused when its metadata part arrives, before any payload is read
/// </summary>
[TestFixture]
public class UniqueIdUploadTests : V2Fixture
{
    [Test]
    public async Task ATakenUniqueIdIsRefusedBeforeThePayloadIsRead()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);
        var uniqueId = Guid.NewGuid();

        var first = await Create(owner, spec.TargetDrive, uniqueId, SamplePayloadDefinitions.GetPayloadDefinition1());
        Assert.That(first.IsSuccessStatusCode, Is.True, $"actual {first.StatusCode}: {first.Error?.Content}");

        // The second payload's bytes don't match its hash: had the payload been read first, this upload would be
        // refused with PayloadHashMismatch instead
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1().WithHash(ContentHashAlgorithm.Blake3);
        payload.Content = "not the bytes that were hashed".ToUtf8ByteArray();

        var refused = await Create(owner, spec.TargetDrive, uniqueId, payload);
        DriveAsserts.AssertRejected(refused, OdinClientErrorCode.ExistingFileWithUniqueId);

        // The rest of the body is unread, so the server says it closes the connection rather than draining it
        Assert.That(refused.Headers.Connection, Does.Contain("close"),
            $"actual Connection header: [{string.Join(", ", refused.Headers.Connection)}]");

        var files = await owner.Drives.Reader.GetBatchAsync(spec.TargetDrive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { ClientUniqueIdAtLeastOne = [uniqueId] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
        });
        var fileIds = files.Content!.SearchResults.Select(f => f.FileId).ToList();
        Assert.That(fileIds, Is.EqualTo(new[] { first.Content!.FileId }), "only the first file holds the uniqueId");

        // Cleanup, not ordering: a refusal after the payload was staged leaves nothing behind either
        var staged = FilesUnder(TenantPaths(owner).GetDriveUploadPath(spec.TargetDrive.Alias));
        Assert.That(staged, Is.Empty, $"the refused upload left files in the upload folder: {string.Join(", ", staged)}");
    }

    private static Task<Refit.ApiResponse<CreateFileResult>> Create(OwnerSession owner, TargetDrive drive, Guid uniqueId,
        TestPayloadDefinition payload)
    {
        var metadata = SampleMetadataData.Create(fileType: 100);
        metadata.AppData.UniqueId = uniqueId;
        return owner.Drives.Writer.CreateNewUnencryptedFile(drive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload]);
    }
}
