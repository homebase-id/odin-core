using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload;

public class PackagePayloadDescriptor
{
    public byte[] Iv { get; set; }

    public string PayloadKey { get; set; }

    public string ContentType { get; set; }

    public long BytesWritten { get; set; }

    public UnixTimeUtc LastModified { get; set; }
    
    public string DescriptorContent { get; set; }
    
    public ThumbnailContent PreviewThumbnail { get; set; }
}