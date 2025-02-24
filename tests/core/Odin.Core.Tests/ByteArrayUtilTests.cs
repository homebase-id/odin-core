using System;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Core.Tests;

public class ByteArrayUtilTests
{
    [Test]
    public void CanDetermineIfKeyIsStrong()
    {
        ClassicAssert.IsFalse(ByteArrayUtil.IsStrongKey(null));
        ClassicAssert.IsFalse(ByteArrayUtil.IsStrongKey(Guid.Empty.ToByteArray()));
        ClassicAssert.IsFalse(ByteArrayUtil.IsStrongKey(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff").ToByteArray()));
        ClassicAssert.IsFalse(ByteArrayUtil.IsStrongKey(Guid.Parse("00000000-1111-1111-2222-222233333333").ToByteArray()));
        ClassicAssert.IsFalse(ByteArrayUtil.IsStrongKey(new byte[17] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3 }));
        ClassicAssert.IsTrue(ByteArrayUtil.IsStrongKey(new byte[17] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4 }));
        ClassicAssert.IsTrue(ByteArrayUtil.IsStrongKey(Guid.Parse("6e39aa0d-3fb9-4e7c-8380-922b42b503a1").ToByteArray()));
    }


    [Test]
    public void TestIntToByteMethods()
    {
        ClassicAssert.IsTrue(ByteArrayUtil.BytesToInt64(ByteArrayUtil.Int64ToBytes(Int64.MaxValue)) == Int64.MaxValue);
        ClassicAssert.IsTrue(ByteArrayUtil.BytesToInt32(ByteArrayUtil.Int32ToBytes(Int32.MaxValue)) == Int32.MaxValue);
        ClassicAssert.IsTrue(ByteArrayUtil.BytesToInt16(ByteArrayUtil.Int16ToBytes(Int16.MaxValue)) == Int16.MaxValue);
        ClassicAssert.IsTrue(ByteArrayUtil.BytesToInt8(ByteArrayUtil.Int8ToBytes(sbyte.MaxValue)) == sbyte.MaxValue);
        Assert.Pass();
    }
}