using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public static class SamplePayloadDefinitions
{
    public static TestPayloadDefinition PayloadDefinitionWithThumbnail1 =
        new()
        {
            Key = "test_key_1",
            Iv = ByteArrayUtil.GetRndByteArray(16),
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
            Key = "test_key_2",
            Iv = ByteArrayUtil.GetRndByteArray(16),
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
            Key = "pknt0001",
            Iv = ByteArrayUtil.GetRndByteArray(16),
            ContentType = "text/plain",
            Content = "a payload of stuff #1".ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = new List<ThumbnailContent>()
        };

    public static TestPayloadDefinition PayloadDefinition2 =
        new()
        {
            Key = "pknt0002",
            Iv = ByteArrayUtil.GetRndByteArray(16),
            ContentType = "text/plain",
            Content = "a payload of stuff #2".ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = new List<ThumbnailContent>()
        };
}