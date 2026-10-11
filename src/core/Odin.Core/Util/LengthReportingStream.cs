using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Util;

#nullable enable

/// <summary>
/// A forward-only stream of known length that HttpClient can measure. <c>StreamContent</c> sends a Content-Length only
/// for a stream that can seek, so a stream that knows its length but cannot seek (an S3 response, #1892) would turn a
/// request chunked. This one reports seekable and its length, and supports only the seek <c>StreamContent</c> makes
/// before anything is read: to where it already is. Rewinding after a read (a retried send) is refused, as it would be
/// for the inner stream. It owns the inner stream.
/// </summary>
public sealed class LengthReportingStream(Stream inner) : Stream
{
    private readonly long _length = inner.Length;
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => _length + offset
        };

        if (target != _position)
        {
            throw new NotSupportedException("A forward-only stream can only 'seek' to where it already is");
        }

        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = inner.Read(buffer);
        _position += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await inner.ReadAsync(buffer, cancellationToken);
        _position += n;
        return n;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
