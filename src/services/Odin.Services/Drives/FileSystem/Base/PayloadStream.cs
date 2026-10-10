using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base;

#nullable enable

public class PayloadStream : IDisposable
{
    /// <param name="contentLength">The number of bytes <paramref name="stream"/> yields: the requested range, not the
    /// payload's size (<see cref="PayloadDescriptor.BytesWritten"/>), when only a range was opened.</param>
    public PayloadStream(PayloadDescriptor descriptor, long contentLength, Stream stream)
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

    /// <summary>
    /// The byte range this stream yields, resolved against the payload (clamped, exact length); null for the whole
    /// payload. With <see cref="PayloadSize"/> it is what a Content-Range header says.
    /// </summary>
    public FileChunk? Range { get; init; }

    /// <summary>The whole payload's size, when <see cref="Range"/> is set.</summary>
    public Int64 PayloadSize { get; init; }

    public UnixTimeUtc LastModified { get; }
    
    public string Key { get; }
    public string ContentType { get; }
    public long ContentLength { get; }
    public Stream Stream { get; }

    /// <summary>
    /// Throws when the payload is larger than <paramref name="maxBytes"/>. Call it before anything that holds the
    /// whole payload in memory: payloads are unbounded, so that is only safe under a cap (#1892).
    /// </summary>
    public void AssertAtMost(Int64 maxBytes)
    {
        if (ContentLength > maxBytes)
        {
            throw new OdinPayloadTooLargeException(
                $"Payload '{Key}' is {ContentLength} bytes, over the {maxBytes} bytes allowed for reading it whole");
        }
    }

    /// <summary>
    /// Reads the whole payload, refusing one larger than <paramref name="maxBytes"/> before reading a byte.
    /// </summary>
    public async Task<byte[]> ReadAllBytesAsync(Int64 maxBytes, CancellationToken cancellationToken = default)
    {
        AssertAtMost(maxBytes);
        var bytes = new byte[ContentLength];
        await Stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

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
