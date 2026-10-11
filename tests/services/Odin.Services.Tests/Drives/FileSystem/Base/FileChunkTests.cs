using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Tests.Drives.FileSystem.Base;

public class FileChunkTests
{
    [TestCase(0, 10, 0, 10)]
    [TestCase(5, null, 5, 95)]
    [TestCase(90, 50, 90, 10)] // past the end: clamped (RFC 9110 14.1.2)
    [TestCase(99, long.MaxValue, 99, 1)]
    [TestCase(0, null, 0, 100)]
    public void ResolveAgainstClampsToThePayload(long start, long? length, long expectedStart, long expectedLength)
    {
        var resolved = new FileChunk { Start = start, Length = length }.ResolveAgainst(100);

        Assert.That(resolved.Start, Is.EqualTo(expectedStart));
        Assert.That(resolved.Length, Is.EqualTo(expectedLength));
    }

    [TestCase(100)]
    [TestCase(3000000000)]
    public void AStartAtOrPastTheEndIsNotSatisfiable(long start)
    {
        var e = Assert.Throws<OdinRangeNotSatisfiableException>(() => new FileChunk { Start = start }.ResolveAgainst(100));
        Assert.That(e!.Size, Is.EqualTo(100L));
    }

    [Test]
    public void AnyRangeOfAnEmptyPayloadIsNotSatisfiable()
    {
        Assert.Throws<OdinRangeNotSatisfiableException>(() => new FileChunk { Start = 0 }.ResolveAgainst(0));
    }

    [TestCase(-1, null)]
    [TestCase(0, 0L)]
    [TestCase(0, -5L)]
    public void AMalformedRangeIsABadRequest(long start, long? length)
    {
        var e = Assert.Throws<OdinClientException>(() => new FileChunk { Start = start, Length = length }.ResolveAgainst(100));
        Assert.That(e, Is.Not.InstanceOf<OdinRangeNotSatisfiableException>());
    }
}
