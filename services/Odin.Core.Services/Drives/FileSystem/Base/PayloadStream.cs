using System.IO;
using Dawn;

namespace Odin.Core.Services.Drives.FileSystem.Base;

public class PayloadStream
{
    public PayloadStream(string payloadKey, string contentType, Stream stream)
    {
        Guard.Argument(payloadKey, nameof(payloadKey)).NotNull().NotEmpty();
        Guard.Argument(contentType, nameof(contentType)).NotNull().NotEmpty();

        Key = payloadKey;
        ContentType = contentType;
        Stream = stream;
    }

    public string Key { get; init; }
    public string ContentType { get; init; }
    public Stream Stream { get; init; }
}