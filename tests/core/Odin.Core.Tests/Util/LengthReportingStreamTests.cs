using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

#nullable enable

public class LengthReportingStreamTests
{
    private static readonly byte[] Data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();

    [Test]
    public void AMultipartRequestOfAStreamThatCannotSeekIsChunked()
    {
        // Why the wrapper exists: HttpClient measures only seekable streams
        using var content = new MultipartFormDataContent { { new StreamContent(new ForwardOnly(Data)), "payload", "p" } };
        Assert.That(content.Headers.ContentLength, Is.Null);
    }

    [Test]
    public async Task HttpClientMeasuresItAndSendsItsBytes()
    {
        using var part = new StreamContent(new LengthReportingStream(new ForwardOnly(Data)));
        Assert.That(part.Headers.ContentLength, Is.EqualTo(Data.Length));

        using var content = new MultipartFormDataContent { { new StreamContent(new LengthReportingStream(new ForwardOnly(Data))), "payload", "p" } };
        Assert.That(content.Headers.ContentLength, Is.Not.Null, "the whole multipart request has a Content-Length");

        Assert.That(await part.ReadAsByteArrayAsync(), Is.EqualTo(Data));
    }

    [Test]
    public void SeeksOnlyToWhereItIs()
    {
        using var sut = new LengthReportingStream(new ForwardOnly(Data));
        Assert.That(sut.Seek(0, SeekOrigin.Begin), Is.EqualTo(0));

        sut.ReadExactly(new byte[10]);
        Assert.That(sut.Position, Is.EqualTo(10));
        Assert.Throws<NotSupportedException>(() => sut.Position = 0, "a rewind after a read is refused");
    }

    [Test]
    public void DisposingDisposesTheInnerStream()
    {
        var inner = new ForwardOnly(Data);
        new LengthReportingStream(inner).Dispose();
        Assert.That(inner.Disposed, Is.True);
    }

    // Knows its length, cannot seek: like an S3 response
    private sealed class ForwardOnly(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
