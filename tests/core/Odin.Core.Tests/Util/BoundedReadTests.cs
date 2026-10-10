using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

#nullable enable

public class BoundedReadTests
{
    [Test]
    public async Task ReadsAStreamUpToTheLimit()
    {
        var bytes = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();

        Assert.That(await BoundedRead.ReadAllBytesAsync(new MemoryStream(bytes), maxBytes: 1000), Is.EqualTo(bytes));
    }

    [Test]
    public void RefusesAStreamPastTheLimitWithoutReadingItAll()
    {
        var endless = new EndlessStream();

        var e = Assert.ThrowsAsync<OdinClientException>(() => BoundedRead.ReadAllBytesAsync(endless, maxBytes: 100_000));

        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.MaxContentLengthExceeded));
        Assert.That(endless.BytesRead, Is.LessThan(100_000 + 64 * 1024), "it must stop reading at the limit");
    }

    [Test]
    public async Task DeserializesASectionWithAByteOrderMark()
    {
        var json = "{\"name\":\"Frodo – ringbearer\"}";
        var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();

        var section = await BoundedRead.DeserializeAsync<Named>(new MemoryStream(withBom));

        Assert.That(section!.Name, Is.EqualTo("Frodo – ringbearer"));
    }

    [Test]
    public void RefusesASectionPastTheLimitWhileDeserializing()
    {
        // A JSON string that never ends: the deserializer keeps reading until the limit stops it
        var endless = new EndlessStream(first: (byte)'"', rest: (byte)'a');

        var e = Assert.ThrowsAsync<OdinClientException>(() => BoundedRead.DeserializeAsync<Named>(endless, maxBytes: 100_000));

        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.MaxContentLengthExceeded));
        Assert.That(endless.BytesRead, Is.LessThan(100_000 + 64 * 1024), "it must stop reading at the limit");
    }

    private sealed class Named
    {
        public string? Name { get; set; }
    }

    // A sender that never stops
    private sealed class EndlessStream(byte first = 0, byte rest = 0) : Stream
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill(rest);
            if (BytesRead == 0 && count > 0)
            {
                buffer[offset] = first;
            }

            BytesRead += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
