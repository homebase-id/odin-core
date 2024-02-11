using System.IO;

using Microsoft.Extensions.Primitives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base;

public class PayloadStream
{
    public PayloadStream(PayloadDescriptor descriptor, Stream stream)
    {
        Key = descriptor.Key;
        ContentType = descriptor.ContentType;
        LastModified = descriptor.LastModified;
        Stream = stream;
    }

    public PayloadStream(string payloadKey, string contentType, UnixTimeUtc lastModified, Stream stream)
    {
        Key = payloadKey;
        ContentType = contentType;
        LastModified = lastModified;
        Stream = stream;
    }

    public UnixTimeUtc LastModified { get; set; }
    
    public string Key { get; init; }
    public string ContentType { get; init; }
    public Stream Stream { get; init; }
}