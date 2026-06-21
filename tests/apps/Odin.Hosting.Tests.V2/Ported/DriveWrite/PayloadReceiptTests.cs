using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Verifies the V2 create/update file responses echo a receipt per uploaded payload whose
/// uid/lastModified are identical to the values serialized in the stored file header — the
/// contract clients rely on to version-address thumbnail caches at upload time.
/// </summary>
[TestFixture]
public class PayloadReceiptTests : V2Fixture
{
    [Test]
    public async Task CreateFile_EchoesPayloadReceipts_MatchingHeader()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100);
        var payloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinition2()
        };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var upload = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata, manifest, payloads);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var uploadResult = upload.Content!;

        Assert.That(uploadResult.Payloads.Count, Is.EqualTo(2));
        Assert.That(uploadResult.Payloads.Select(r => r.Key), Is.EquivalentTo(payloads.Select(p => p.Key)));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(uploadResult.DriveId, uploadResult.FileId)).Content!;
        AssertReceiptsMatchHeader(uploadResult.Payloads, header.FileMetadata.Payloads);
    }

    [Test]
    public async Task UpdateFile_EchoesPayloadReceipts_ForUploadedPayloadsOnly()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        // Seed: file with two payloads (key 1 will be overwritten, key 2 left untouched)
        var seed = SampleMetadataData.Create(fileType: 100);
        var seedPayloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinition2()
        };
        var seedManifest = new UploadManifest { PayloadDescriptors = seedPayloads.ToPayloadDescriptorList().ToList() };
        var upload = await owner.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, seed, seedManifest, seedPayloads);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var uploadResult = upload.Content!;
        var seedReceipts = uploadResult.Payloads;

        var overwrittenPayload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        overwrittenPayload.Content = "overwritten content for payload key 1".ToUtf8ByteArray();
        var appendedPayload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var untouchedKey = seedPayloads[1].Key;

        var updated = seed;
        updated.AppData.Content = "some new content here";
        updated.VersionTag = uploadResult.NewVersionTag;

        var instructionSet = new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Recipients = null,
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    overwrittenPayload.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite),
                    appendedPayload.ToPayloadDescriptor(PayloadUpdateOperationType.AppendOrOverwrite)
                ]
            }
        };

        var response = await owner.Drives.Writer.UpdateFileByFileId(
            uploadResult.DriveId, uploadResult.FileId, instructionSet, updated, [overwrittenPayload, appendedPayload]);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"actual {response.StatusCode}");
        var updateResult = response.Content!;

        // Only the payloads uploaded in this request are echoed; the untouched seed payload is not
        Assert.That(updateResult.Payloads.Select(r => r.Key),
            Is.EquivalentTo(new[] { overwrittenPayload.Key, appendedPayload.Key }));

        var header = (await owner.Drives.Reader.GetFileHeaderAsync(uploadResult.DriveId, uploadResult.FileId)).Content!;
        Assert.That(header.FileMetadata.Payloads.Count, Is.EqualTo(3));
        AssertReceiptsMatchHeader(updateResult.Payloads, header.FileMetadata.Payloads);

        // The overwritten payload got a new uid and lastModified
        var seedReceipt = seedReceipts.Single(r => r.Key == overwrittenPayload.Key);
        var newReceipt = updateResult.Payloads.Single(r => r.Key == overwrittenPayload.Key);
        Assert.That(newReceipt.Uid.uniqueTime, Is.Not.EqualTo(seedReceipt.Uid.uniqueTime));
        Assert.That(newReceipt.LastModified.milliseconds, Is.GreaterThan(seedReceipt.LastModified.milliseconds));

        // The untouched payload kept its original header values
        var untouchedDescriptor = header.FileMetadata.Payloads.Single(p => p.Key == untouchedKey);
        var untouchedSeedReceipt = seedReceipts.Single(r => r.Key == untouchedKey);
        Assert.That(untouchedDescriptor.Uid.uniqueTime, Is.EqualTo(untouchedSeedReceipt.Uid.uniqueTime));
        Assert.That(untouchedDescriptor.LastModified.milliseconds, Is.EqualTo(untouchedSeedReceipt.LastModified.milliseconds));
    }

    [Test]
    public async Task MetadataOnlyCreateAndUpdate_ReturnEmptyPayloadReceipts()
    {
        var spec = CallerSpec.Owner(DriveSpec.Anon());
        var (_, owner) = await SetupCallerWithOwner(spec);

        var seed = SampleMetadataData.Create(fileType: 100);
        var upload = await owner.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, seed);
        Assert.That(upload.IsSuccessStatusCode, Is.True);
        var uploadResult = upload.Content!;
        Assert.That(uploadResult.Payloads, Is.Empty);

        var updated = seed;
        updated.AppData.Content = "some new content here...";
        updated.VersionTag = uploadResult.NewVersionTag;

        var instructionSet = new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Recipients = null,
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        };

        var response = await owner.Drives.Writer.UpdateFileByFileId(
            uploadResult.DriveId, uploadResult.FileId, instructionSet, updated, []);
        Assert.That(response.IsSuccessStatusCode, Is.True);
        Assert.That(response.Content!.Payloads, Is.Empty);
    }

    /// <summary>
    /// Per key, the receipt's uid/lastModified must equal the header's payload descriptor values.
    /// Both sides round-tripped through the same JSON converters (plain numbers), so value equality
    /// here is wire-level equality of the serialized fields.
    /// </summary>
    private static void AssertReceiptsMatchHeader(
        IEnumerable<PayloadUploadReceipt> receipts,
        IEnumerable<PayloadDescriptor> headerDescriptors)
    {
        foreach (var receipt in receipts)
        {
            var descriptor = headerDescriptors.SingleOrDefault(p => p.Key == receipt.Key);
            Assert.That(descriptor, Is.Not.Null, $"header has no payload descriptor for key {receipt.Key}");
            Assert.That(receipt.Uid.uniqueTime, Is.EqualTo(descriptor!.Uid.uniqueTime), $"uid mismatch for key {receipt.Key}");
            Assert.That(receipt.LastModified.milliseconds, Is.EqualTo(descriptor.LastModified.milliseconds),
                $"lastModified mismatch for key {receipt.Key}");
            Assert.That(receipt.LastModified.milliseconds, Is.GreaterThan(0));
        }
    }
}
