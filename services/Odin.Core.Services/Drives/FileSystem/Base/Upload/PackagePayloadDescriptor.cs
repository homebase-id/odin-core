using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload;

public class PackagePayloadDescriptor
{
    public string PayloadKey { get; set; }

    public string ContentType { get; set; }

    public long BytesWritten { get; set; }

    public UnixTimeUtc LastModified { get; set; }
    public string DescriptorContent { get; set; }
}