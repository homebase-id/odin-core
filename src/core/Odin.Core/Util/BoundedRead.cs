using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

namespace Odin.Core.Util;

#nullable enable

/// <summary>
/// Reads of request parts that have to be held whole -- the JSON sections of a multipart upload -- under a bound the
/// server sets, not the sender. Kestrel does not bound the request body (<c>MaxRequestBodySize = null</c>, for
/// payloads), so without this a sender decides how much memory a section takes (#1892).
/// </summary>
public static class BoundedRead
{
    /// <summary>
    /// Bound on one JSON section (metadata, instructions, key header). Real ones are far smaller: a header's metadata
    /// is capped at 60,000 characters in the database.
    /// </summary>
    public const int MaxJsonSectionBytes = 4 * 1024 * 1024;

    /// <summary>
    /// <paramref name="stream"/>, refusing it with a 400 as soon as more than <paramref name="maxBytes"/> are read
    /// from it. Pass it straight to a deserializer: the bound holds without an extra copy of the bytes.
    /// </summary>
    public static Stream Limit(Stream stream, long maxBytes) => new LimitedReadStream(stream, maxBytes);

    /// <summary>
    /// Deserializes a JSON section, refusing it with a 400 past <paramref name="maxBytes"/>.
    /// </summary>
    public static Task<T?> DeserializeAsync<T>(Stream stream, int maxBytes = MaxJsonSectionBytes,
        CancellationToken cancellationToken = default)
        => OdinSystemSerializer.Deserialize<T>(Limit(stream, maxBytes), cancellationToken);

    /// <summary>
    /// Reads <paramref name="stream"/> to the end, refusing it with a 400 as soon as it passes
    /// <paramref name="maxBytes"/>, so it never buffers more than that.
    /// </summary>
    public static async Task<byte[]> ReadAllBytesAsync(Stream stream, int maxBytes = MaxJsonSectionBytes,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await Limit(stream, maxBytes).CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// Reads a stream of known <paramref name="length"/> whole, or returns null, without reading a byte, when that is
    /// over <paramref name="maxBytes"/>: for a caller that skips or falls back rather than fails.
    /// </summary>
    public static async Task<byte[]?> TryReadAllBytesAsync(Stream stream, long length, long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (length > maxBytes)
        {
            return null;
        }

        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    // Forward-only; counts what is read through it. It does not own the inner stream.
    private sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Count(await inner.ReadAsync(buffer, cancellationToken));

        private int Count(int n)
        {
            _read += n;
            if (_read > maxBytes)
            {
                throw new OdinClientException($"Request part exceeds the {maxBytes} byte limit",
                    OdinClientErrorCode.MaxContentLengthExceeded);
            }

            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
