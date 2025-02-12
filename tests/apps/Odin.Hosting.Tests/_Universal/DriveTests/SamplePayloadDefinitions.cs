using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests;

public static class SamplePayloadDefinitions
{
    public static TestPayloadDefinition GetPayloadDefinitionWithThumbnail1()
    {
        return new()
        {
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
    }

    public static TestPayloadDefinition GetPayloadDefinitionWithThumbnail2()
    {
        return new()
        {
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
    }

    public static TestPayloadDefinition GetPayloadDefinition1()
    {
        return new()
        {
            Key = "pknt0001",
            ContentType = "text/plain",
            Content = "a payload of stuff #1".ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = new List<ThumbnailContent>()
        };
    }

    public static TestPayloadDefinition GetPayloadDefinition2()
    {
        return new()
        {
            Key = "pknt0002",
            ContentType = "text/plain",
            Content = "a payload of stuff #2".ToUtf8ByteArray(),
            DescriptorContent = "",
            PreviewThumbnail = default,
            Thumbnails = new List<ThumbnailContent>()
        };
    }
}