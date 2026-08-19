using System.Collections.Generic;
using NUnit.Framework;
using Odin.Services.Contacts;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Pure-logic tests for <see cref="PeerContactImage"/>'s digest — the value recorded on the stored
/// payload descriptor that lets a repeat sync of an unchanged photo be a no-op instead of a rewrite.
/// No host required.
/// </summary>
[TestFixture]
public class PeerContactImageTests
{
    private static PeerContactImage Image(byte[] content, params PeerContactImageThumbnail[] thumbnails)
    {
        return new PeerContactImage
        {
            Content = content,
            ContentType = "image/jpeg",
            Thumbnails = new List<PeerContactImageThumbnail>(thumbnails)
        };
    }

    private static PeerContactImageThumbnail Thumb(int size, byte[] content)
    {
        return new PeerContactImageThumbnail
        {
            PixelWidth = size,
            PixelHeight = size,
            ContentType = "image/jpeg",
            Content = content
        };
    }

    [Test]
    public void ComputeHash_IsStableForIdenticalImages()
    {
        var a = Image([1, 2, 3], Thumb(32, [9, 9]));
        var b = Image([1, 2, 3], Thumb(32, [9, 9]));

        Assert.That(a.ComputeHash(), Is.EqualTo(b.ComputeHash()), "same bytes → same digest → sync is a no-op");
    }

    [Test]
    public void ComputeHash_ChangesWhenImageChanges()
    {
        var a = Image([1, 2, 3]);
        var b = Image([1, 2, 4]);

        Assert.That(a.ComputeHash(), Is.Not.EqualTo(b.ComputeHash()));
    }

    [Test]
    public void ComputeHash_ChangesWhenOnlyAThumbnailChanges()
    {
        var a = Image([1, 2, 3], Thumb(32, [9, 9]));
        var b = Image([1, 2, 3], Thumb(32, [9, 8]));

        Assert.That(a.ComputeHash(), Is.Not.EqualTo(b.ComputeHash()),
            "a rendition change must still trigger a rewrite");
    }

    [Test]
    public void ComputeHash_ChangesWhenThumbnailDimensionsChange()
    {
        var a = Image([1, 2, 3], Thumb(32, [9, 9]));
        var b = Image([1, 2, 3], Thumb(64, [9, 9]));

        Assert.That(a.ComputeHash(), Is.Not.EqualTo(b.ComputeHash()));
    }

    [Test]
    public void ComputeHash_IgnoresThumbnailOrder()
    {
        var a = Image([1, 2, 3], Thumb(32, [9]), Thumb(64, [8]));
        var b = Image([1, 2, 3], Thumb(64, [8]), Thumb(32, [9]));

        Assert.That(a.ComputeHash(), Is.EqualTo(b.ComputeHash()),
            "the peer's listing order must not force a pointless rewrite");
    }

    [Test]
    public void ComputeHash_DistinguishesThumbnailBoundaries()
    {
        // Same concatenated bytes, different split — the length prefix must keep these apart.
        var a = Image([1, 2, 3], Thumb(32, [9, 9]));
        var b = Image([1, 2, 3, 9], Thumb(32, [9]));

        Assert.That(a.ComputeHash(), Is.Not.EqualTo(b.ComputeHash()));
    }

    [Test]
    public void None_IsARemoval()
    {
        Assert.That(PeerContactImage.None.IsRemoval, Is.True);
        Assert.That(Image([1]).IsRemoval, Is.False);
        Assert.That(Image([]).IsRemoval, Is.True, "empty content is a removal, not a zero-byte photo");
    }
}
