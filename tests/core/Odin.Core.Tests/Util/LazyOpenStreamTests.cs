using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

#nullable enable

public class LazyOpenStreamTests
{
    [Test]
    public async Task OpensOnTheFirstReadOnly()
    {
        var opens = 0;
        var inner = new MemoryStream([1, 2, 3]);
        await using var sut = new LazyOpenStream(() =>
        {
            opens++;
            return Task.FromResult<Stream>(inner);
        });
        Assert.That(opens, Is.EqualTo(0), "must not open before it is read");

        using var copy = new MemoryStream();
        await sut.CopyToAsync(copy);

        Assert.That(opens, Is.EqualTo(1));
        Assert.That(copy.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public async Task DisposingDisposesTheOpenedStreamAndIsSafeUnopened()
    {
        var inner = new MemoryStream([1]);
        var sut = new LazyOpenStream(() => Task.FromResult<Stream>(inner));
        await sut.ReadExactlyAsync(new byte[1]);
        await sut.DisposeAsync();
        Assert.That(inner.CanRead, Is.False);

        Assert.DoesNotThrow(() => new LazyOpenStream(() => throw new InvalidOperationException("never opened")).Dispose());
    }

    [Test]
    public void KeepsAFailedOpenForTheCallerToReport()
    {
        var failure = new FileNotFoundException("payload missing from storage");
        var sut = new LazyOpenStream(() => Task.FromException<Stream>(failure));

        Assert.ThrowsAsync<FileNotFoundException>(async () => await sut.ReadExactlyAsync(new byte[1]));
        Assert.That(sut.Failure, Is.SameAs(failure));
    }

    [Test]
    public void KeepsAFailedReadForTheCallerToReport()
    {
        var failure = new IOException("S3 body reset part way");
        var sut = new LazyOpenStream(() => Task.FromResult<Stream>(new FailingStream(failure)));

        Assert.ThrowsAsync<IOException>(async () => await sut.ReadExactlyAsync(new byte[1]));
        Assert.That(sut.Failure, Is.SameAs(failure));
    }

    private sealed class FailingStream(Exception failure) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw failure;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
