using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;

namespace Odin.Core.Tests.Util;

#nullable enable

public class ReadOnlyWindowStreamTests
{
    private static readonly byte[] Data = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();

    [Test]
    public async Task ReadsOnlyTheWindowFromTheInnerPosition()
    {
        var inner = new MemoryStream(Data) { Position = 10 };
        await using var sut = new ReadOnlyWindowStream(inner, 20);

        using var copy = new MemoryStream();
        await sut.CopyToAsync(copy, bufferSize: 7);

        Assert.That(sut.Length, Is.EqualTo(20));
        Assert.That(sut.Position, Is.EqualTo(20));
        Assert.That(copy.ToArray(), Is.EqualTo(Data[10..30]));
        Assert.That(inner.Position, Is.EqualTo(30), "must not read past the window");
    }

    [Test]
    public void SyncReadsStopAtTheWindow()
    {
        using var sut = new ReadOnlyWindowStream(new MemoryStream(Data), 5);

        var buffer = new byte[50];
        Assert.That(sut.Read(buffer, 0, buffer.Length), Is.EqualTo(5));
        Assert.That(buffer[..5], Is.EqualTo(Data[..5]));
        Assert.That(sut.Read(buffer, 0, buffer.Length), Is.EqualTo(0));
    }

    [Test]
    public async Task AWindowPastTheInnerEndYieldsWhatIsThere()
    {
        // Callers clamp the window to the file; this documents what happens if they don't
        await using var sut = new ReadOnlyWindowStream(new MemoryStream(Data) { Position = 95 }, 20);

        using var copy = new MemoryStream();
        await sut.CopyToAsync(copy);

        Assert.That(copy.ToArray(), Is.EqualTo(Data[95..]));
    }

    [Test]
    public void IsForwardOnly()
    {
        using var sut = new ReadOnlyWindowStream(new MemoryStream(Data), 5);

        Assert.That(sut.CanSeek, Is.False);
        Assert.That(sut.CanWrite, Is.False);
        Assert.Throws<NotSupportedException>(() => sut.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => sut.Position = 0);
    }

    [Test]
    public async Task DisposingDisposesTheInnerStream()
    {
        var inner = new MemoryStream(Data);
        await new ReadOnlyWindowStream(inner, 5).DisposeAsync();
        Assert.That(inner.CanRead, Is.False);

        inner = new MemoryStream(Data);
        new ReadOnlyWindowStream(inner, 5).Dispose();
        Assert.That(inner.CanRead, Is.False);
    }

    [Test]
    public void RejectsANegativeLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ReadOnlyWindowStream(new MemoryStream(Data), -1));
    }
}
