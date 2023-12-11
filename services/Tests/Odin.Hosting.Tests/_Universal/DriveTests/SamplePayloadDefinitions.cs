using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public static class SamplePayloadDefinitions
{
    public static TestPayloadDefinition PayloadDefinitionWithThumbnail1 =
        new()
        {
            Iv = Guid.Parse("211cecef-fe55-4e28-aa4d-d8b9d577644e").ToByteArray(),
            Key = "test_key_1",
            ContentType = "text/plain",
            Content = "some content for payload key 1".ToUtf8ByteArray(),
            PreviewThumbnail = new ThumbnailContent()
            {
                PixelHeight = 100,
                PixelWidth = 100,
                ContentType = "image/png",
                Content = TestMedia.PreviewPngThumbnailBytes
            },
            Thumbnails = new List<ThumbnailContent>()
            {
                new()
                {
                    PixelHeight = 200,
                    PixelWidth = 200,
                    ContentType = "image/png",
                    Content = TestMedia.ThumbnailBytes200,
                }
            }
        };

    public static TestPayloadDefinition PayloadDefinitionWithThumbnail2 =
        new()
        {
            Iv = Guid.Parse("8f5aa163-6434-444d-8f05-3fe796bcf5cc").ToByteArray(),
            Key = "test_key_2",
            ContentType = "text/plain",
            Content = "other types of content for key 2".ToUtf8ByteArray(),
            PreviewThumbnail = new ThumbnailContent()
            {
                PixelHeight = 100,
                PixelWidth = 100,
                ContentType = "image/png",
                Content = TestMedia.PreviewPngThumbnailBytes
            },
            Thumbnails = new List<ThumbnailContent>()
            {
                new()
                {
                    PixelHeight = 400,
                    PixelWidth = 400,
                    ContentType = "image/png",
                    Content = TestMedia.ThumbnailBytes400,
                }
            }
        };

    public static TestPayloadDefinition PayloadDefinition1 =
        new()
        {
            Iv = Guid.Parse("210edf3b-0a82-4041-8b8f-95e50a8c3155").ToByteArray(),
            Key = "pknt0001",
            ContentType = "text/plain",
            Content = "a payload of stuff #1".ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = new List<ThumbnailContent>()
        };

    public static TestPayloadDefinition PayloadDefinition2 =
        new()
        {
            Iv = Guid.Parse("3f99ae60-7369-4733-9a5a-d54db6487ef3").ToByteArray(),
            Key = "pknt0002",
            ContentType = "text/plain",
            Content = "a payload of stuff #2".ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = new List<ThumbnailContent>()
        };
}