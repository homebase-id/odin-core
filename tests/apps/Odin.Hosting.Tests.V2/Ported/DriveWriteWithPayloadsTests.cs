using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported;

/// <summary>
/// Port of <c>_V2/Tests/Drive/WriteFileTests/DiretDriveWriteNewFileTestsV2.CanUploadFileWith2PayloadsAnd2Thumbnails</c>.
/// Exercises the full multipart upload path (instruction set + encrypted descriptor + two payloads,
/// each with two thumbnails) across the Owner / App / Guest matrix, then — for the success cases —
/// re-reads as owner and verifies the header, payload bytes, and thumbnail bytes round-trip intact.
/// </summary>
[TestFixture]
public class DriveWriteWithPayloadsTests : V2Fixture
{
    public static IEnumerable<object[]> WriteCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUploadFileWith2PayloadsAnd2Thumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var metadata = SampleMetadataData.Create(fileType: 100);
        var payloads = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var response = await caller.Drives.Writer.CreateNewUnencryptedFile(spec.TargetDrive.Alias, metadata, manifest, payloads);

        Assert.That(response.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var uploadResult = response.Content!;

        var headerResponse = await owner.Drives.Reader.GetFileHeaderAsync(uploadResult.DriveId, uploadResult.FileId);
        Assert.That(headerResponse.IsSuccessStatusCode, Is.True);
        var header = headerResponse.Content!;
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(metadata.AppData.Content));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(payloads.Count));

        foreach (var testPayload in payloads)
        {
            var headerPayload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
            Assert.That(headerPayload.Thumbnails.Count, Is.EqualTo(testPayload.Thumbnails.Count));
            Assert.That(headerPayload.ContentType, Is.EqualTo(testPayload.ContentType));
            Assert.That(ByteArrayUtil.EquiByteArrayCompare(testPayload.Iv, headerPayload.Iv), Is.True);
        }

        foreach (var definition in payloads)
        {
            var payloadResponse = await owner.Drives.Reader.GetPayloadAsync(uploadResult.DriveId, uploadResult.FileId, definition.Key);
            Assert.That(payloadResponse.IsSuccessStatusCode, Is.True);
            Assert.That(payloadResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(payloadResponse.ContentHeaders.LastModified!.Value, Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var content = await ReadAllBytesAsync(payloadResponse.Content!);
            Assert.That(content, Is.EqualTo(definition.Content));

            foreach (var thumbnail in definition.Thumbnails)
            {
                var thumbResponse = await owner.Drives.Reader.GetThumbnailAsync(
                    uploadResult.DriveId, uploadResult.FileId, definition.Key,
                    thumbnail.PixelWidth, thumbnail.PixelHeight);

                Assert.That(thumbResponse.IsSuccessStatusCode, Is.True);
                Assert.That(thumbResponse.ContentHeaders!.LastModified.HasValue, Is.True);
                Assert.That(thumbResponse.ContentHeaders.LastModified!.Value, Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

                var thumbContent = await ReadAllBytesAsync(thumbResponse.Content!);
                Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
            }
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(HttpContent content)
    {
        await using var stream = await content.ReadAsStreamAsync();
        return stream.ToByteArray();
    }
}
