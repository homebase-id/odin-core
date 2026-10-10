using System;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Core.Util;
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
    /// The byte range this stream yields within the whole payload, as its Content-Range header says it; null when
    /// the stream is the whole payload.
    /// </summary>
    public ContentRangeHeaderValue? ContentRange { get; init; }

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
    /// Reads the whole payload, or returns null, without reading a byte, when it is larger than
    /// <paramref name="maxBytes"/>.
    /// </summary>
    public Task<byte[]?> TryReadAllBytesAsync(Int64 maxBytes, CancellationToken cancellationToken = default)
        => BoundedRead.TryReadAllBytesAsync(Stream, ContentLength, maxBytes, cancellationToken);

    /// <summary>
    /// Reads the whole payload, refusing one larger than <paramref name="maxBytes"/> before reading a byte.
    /// </summary>
    public async Task<byte[]> ReadAllBytesAsync(Int64 maxBytes, CancellationToken cancellationToken = default)
    {
        AssertAtMost(maxBytes);
        return (await TryReadAllBytesAsync(maxBytes, cancellationToken))!;
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
