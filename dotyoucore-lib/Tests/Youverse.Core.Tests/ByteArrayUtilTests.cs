using System;
using NUnit.Framework;

namespace Youverse.Core.Tests;

public class ByteArrayUtilTests
{
    [Test]
    public void CanDetermineIfKeyIsStrong()
    {
        Assert.IsFalse(ByteArrayUtil.IsStrongKey(null));
        Assert.IsFalse(ByteArrayUtil.IsStrongKey(Guid.Empty.ToByteArray()));
        Assert.IsFalse(ByteArrayUtil.IsStrongKey(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff").ToByteArray()));
        Assert.IsFalse(ByteArrayUtil.IsStrongKey(Guid.Parse("00000000-1111-1111-2222-222233333333").ToByteArray()));
        Assert.IsFalse(ByteArrayUtil.IsStrongKey(new byte[17] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3 }));
        Assert.IsTrue (ByteArrayUtil.IsStrongKey(new byte[17] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4 }));
        Assert.IsTrue(ByteArrayUtil.IsStrongKey(Guid.Parse("6e39aa0d-3fb9-4e7c-8380-922b42b503a1").ToByteArray()));
    }
}