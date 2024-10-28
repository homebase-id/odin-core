using System;
using Odin.Core;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload;

public class PackagePayloadDescriptor
{
    public byte[] Iv { get; set; }

    public string PayloadKey { get; set; }

    public string ContentType { get; set; }

    public long BytesWritten { get; set; }

    public UnixTimeUtc LastModified { get; set; }

    public string DescriptorContent { get; set; }

    public ThumbnailContent PreviewThumbnail { get; set; }

    public UnixTimeUtcUnique Uid { get; set; }
    
    public PayloadUpdateOperationType UpdateOperationType { get; set; }

    public bool HasIv()
    {
        if (Iv == null || Iv.Length == 0)
        {
            return false;
        }

        //special case - check for 16 zeros
        return Iv.Length == 16 && new Guid(Iv) != Guid.Empty;
    }

    public bool HasStrongIv()
    {
        return HasIv() && ByteArrayUtil.IsStrongKey(Iv);
    }
}