using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Core.Util;

#nullable enable

/// <summary>
/// Opens its inner stream on the first read and owns it. A request built from many parts then holds open only the
/// part being sent: an S3 response opened up front and left idle behind a long upload can be closed under it.
/// </summary>
/// <remarks>
/// An open that fails is kept on <see cref="OpenException"/>, because the HTTP stack reports it wrapped as a failure
/// to copy the request content, which reads like a network error.
/// </remarks>
public sealed class LazyOpenStream(Func<Task<Stream>> open) : Stream
{
    private Stream? _inner;

    public Exception? OpenException { get; private set; }

    private async ValueTask<Stream> InnerAsync()
    {
        if (_inner != null)
        {
            return _inner;
        }

        try
        {
            _inner = await open();
        }
        catch (Exception e)
        {
            OpenException = e;
            throw;
        }

        return _inner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => InnerAsync().AsTask().GetAwaiter().GetResult().Read(buffer);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => await (await InnerAsync()).ReadAsync(buffer, cancellationToken);

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner?.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_inner != null)
        {
            await _inner.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
