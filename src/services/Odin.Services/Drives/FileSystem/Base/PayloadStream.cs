using System;
using System.IO;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base;

#nullable enable

public class PayloadStream : IDisposable
{
    public PayloadStream(PayloadDescriptor descriptor, long contentLength, Stream stream) // TODO: can we assume contentLength == PayloadDescriptor.BytesWritten ?
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        Key = descriptor.Key;
        ContentType = descriptor.ContentType;
        ContentLength = contentLength;
        LastModified = descriptor.LastModified;
        Stream = stream;
    }

    public PayloadStream(string payloadKey, string contentType, long contentLength, UnixTimeUtc lastModified, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        Key = payloadKey;
        ContentType = contentType;
        ContentLength = contentLength;
        LastModified = lastModified;
        Stream = stream;
    }

    public UnixTimeUtc LastModified { get; }
    
    public string Key { get; }
    public string ContentType { get; }
    public long ContentLength { get; }
    public Stream Stream { get; }

    private bool _disposed;
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Stream.Dispose();
        }
    }
}
