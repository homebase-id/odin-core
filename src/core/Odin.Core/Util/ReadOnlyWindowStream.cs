using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Util;

/// <summary>
/// Forward-only read stream over the next <c>length</c> bytes of <c>inner</c>, from its current position.
/// It owns the inner stream. Length is the window, not the inner stream's length, because callers send it
/// as Content-Length.
/// </summary>
public sealed class ReadOnlyWindowStream : Stream
{
    private readonly Stream _inner;
    private readonly Int64 _length;
    private Int64 _position;

    public ReadOnlyWindowStream(Stream inner, Int64 length)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _inner = inner;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var n = _inner.Read(buffer[..Remaining(buffer.Length)]);
        _position += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var n = await _inner.ReadAsync(buffer[..Remaining(buffer.Length)], cancellationToken);
        _position += n;
        return n;
    }

    private int Remaining(int requested) => (int)Math.Min(requested, _length - _position);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
