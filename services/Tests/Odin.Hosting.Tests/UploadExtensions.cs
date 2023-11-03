using System.Collections.Generic;
using System.Linq;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests;

public static class UploadExtensions
{
    public static IEnumerable<UploadManifestPayloadDescriptor> ToPayloadDescriptorList(this IEnumerable<TestPayloadDefinition> list)
    {
        return list.Select(tpd => tpd.ToPayloadDescriptor());
    }

    public static string GetFilename(this ThumbnailDescriptor descriptor, string payloadKey = WebScaffold.PAYLOAD_KEY)
    {
        return $"{descriptor.PixelWidth}x{descriptor.PixelHeight}-{payloadKey}";
    }
}