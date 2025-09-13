using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Time;

namespace Odin.Core.Storage.Tests;

// Claude was here!

[TestFixture]
public class HashParametersTests
{
    [Test]
    public void Calculate_WithNoParameters_ReturnsValidHash()
    {
        var result = HashParameters.Calculate();

        Assert.That(result, Is.Not.Null.And.Not.Empty);
        Assert.That(result.Length, Is.EqualTo(16)); // 8 bytes = 16 hex chars
        Assert.That(result, Does.Match("^[0-9a-f]+$")); // lowercase hex
    }

    [Test]
    public void Calculate_WithSameParameters_ReturnsSameHash()
    {
        var hash1 = HashParameters.Calculate(42, "test", true);
        var hash2 = HashParameters.Calculate(42, "test", true);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithDifferentParameters_ReturnsDifferentHash()
    {
        var hash1 = HashParameters.Calculate(42, "test");
        var hash2 = HashParameters.Calculate(43, "test");

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithParameterOrderChanged_ReturnsDifferentHash()
    {
        var hash1 = HashParameters.Calculate("test", 42);
        var hash2 = HashParameters.Calculate(42, "test");

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_WithBool_IsConsistent(bool value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase((byte)0)]
    [TestCase((byte)255)]
    [TestCase((byte)128)]
    public void Calculate_WithByte_IsConsistent(byte value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase((sbyte)-128)]
    [TestCase((sbyte)127)]
    [TestCase((sbyte)0)]
    public void Calculate_WithSByte_IsConsistent(sbyte value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase((short)-32768)]
    [TestCase((short)32767)]
    [TestCase((short)0)]
    public void Calculate_WithShort_IsConsistent(short value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase((ushort)0)]
    [TestCase((ushort)65535)]
    public void Calculate_WithUShort_IsConsistent(ushort value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    [TestCase(0)]
    [TestCase(42)]
    public void Calculate_WithInt_IsConsistent(int value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(uint.MinValue)]
    [TestCase(uint.MaxValue)]
    public void Calculate_WithUInt_IsConsistent(uint value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    [TestCase(0L)]
    public void Calculate_WithLong_IsConsistent(long value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(ulong.MinValue)]
    [TestCase(ulong.MaxValue)]
    public void Calculate_WithULong_IsConsistent(ulong value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(0.0f)]
    [TestCase(float.MinValue)]
    [TestCase(float.MaxValue)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Calculate_WithFloat_IsConsistent(float value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(0.0)]
    [TestCase(double.MinValue)]
    [TestCase(double.MaxValue)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void Calculate_WithDouble_IsConsistent(double value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase("79228162514264337593543950335")] // decimal.MaxValue
    [TestCase("-79228162514264337593543950335")] // decimal.MinValue
    [TestCase("0")]
    [TestCase("3.14159")]
    public void Calculate_WithDecimal_IsConsistent(string decimalString)
    {
        var value = decimal.Parse(decimalString);
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase('a')]
    [TestCase('\0')]
    [TestCase('ñ')]
    public void Calculate_WithChar_IsConsistent(char value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase("")]
    [TestCase("test")]
    [TestCase("unicode: ñáéíóú")]
    public void Calculate_WithString_IsConsistent(string value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithByteArray_IsConsistent()
    {
        var bytes = new byte[] { 1, 2, 3, 255, 0 };
        var hash1 = HashParameters.Calculate(bytes);
        var hash2 = HashParameters.Calculate(bytes);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithEmptyByteArray_IsConsistent()
    {
        var bytes = new byte[0];
        var hash1 = HashParameters.Calculate(bytes);
        var hash2 = HashParameters.Calculate(bytes);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    public enum TestEnum { Value1, Value2, Value3 = 42 }

    [TestCase(TestEnum.Value1)]
    [TestCase(TestEnum.Value2)]
    [TestCase(TestEnum.Value3)]
    public void Calculate_WithEnum_IsConsistent(TestEnum value)
    {
        var hash1 = HashParameters.Calculate(value);
        var hash2 = HashParameters.Calculate(value);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithGuid_IsConsistent()
    {
        var guid = Guid.NewGuid();
        var hash1 = HashParameters.Calculate(guid);
        var hash2 = HashParameters.Calculate(guid);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithListOfInt_IsConsistent()
    {
        var list = new List<int> { 1, 2, 3, 42 };
        var hash1 = HashParameters.Calculate(list);
        var hash2 = HashParameters.Calculate(list);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithEmptyListOfInt_IsConsistent()
    {
        var list = new List<int>();
        var hash1 = HashParameters.Calculate(list);
        var hash2 = HashParameters.Calculate(list);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithListOfGuid_IsConsistent()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var list = new List<Guid> { guid1, guid2 };
        var hash1 = HashParameters.Calculate(list);
        var hash2 = HashParameters.Calculate(list);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithListOfString_IsConsistent()
    {
        var list = new List<string> { "hello", "world", "" };
        var hash1 = HashParameters.Calculate(list);
        var hash2 = HashParameters.Calculate(list);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnsupportedType_ThrowsNotSupportedException()
    {
        var unsupported = new object();

        var ex = Assert.Throws<NotSupportedException>(() =>
            HashParameters.Calculate(unsupported));

        Assert.That(ex.Message, Does.Contain("Unsupported type"));
        Assert.That(ex.Message, Does.Contain("System.Object"));
    }

    [Test]
    public void Calculate_WithMixedTypes_IsConsistent()
    {
        var guid = Guid.NewGuid();
        var list = new List<int> { 1, 2, 3 };
        var bytes = new byte[] { 1, 2, 3 };

        var hash1 = HashParameters.Calculate(42, "test", true, null, guid, list, bytes);
        var hash2 = HashParameters.Calculate(42, "test", true, null, guid, list, bytes);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtc_IsConsistent()
    {
        var time = new UnixTimeUtc(1234567890000L);
        var hash1 = HashParameters.Calculate(time);
        var hash2 = HashParameters.Calculate(time);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithDifferentUnixTimeUtc_ReturnsDifferentHash()
    {
        var time1 = new UnixTimeUtc(1234567890000L);
        var time2 = new UnixTimeUtc(1234567890001L);

        var hash1 = HashParameters.Calculate(time1);
        var hash2 = HashParameters.Calculate(time2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcZeroTime_IsConsistent()
    {
        var time = UnixTimeUtc.ZeroTime;
        var hash1 = HashParameters.Calculate(time);
        var hash2 = HashParameters.Calculate(time);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcFromDateTime_IsConsistent()
    {
        var dateTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var time = UnixTimeUtc.FromDateTime(dateTime);
        var hash1 = HashParameters.Calculate(time);
        var hash2 = HashParameters.Calculate(time);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(0L)]
    [TestCase(1234567890000L)]
    [TestCase(-1000L)] // Before epoch
    [TestCase(long.MaxValue)]
    [TestCase(long.MinValue)]
    public void Calculate_WithUnixTimeUtcValues_IsConsistent(long milliseconds)
    {
        var time = new UnixTimeUtc(milliseconds);
        var hash1 = HashParameters.Calculate(time);
        var hash2 = HashParameters.Calculate(time);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithQueryBatchCursor_IsConsistent()
    {
        var cursor = new QueryBatchCursor();
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithQueryBatchCursorFromStartPoint_IsConsistent()
    {
        var timestamp = new UnixTimeUtc(1234567890000L);
        var cursor = QueryBatchCursor.FromStartPoint(timestamp, 42L);
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithQueryBatchCursorWithBoundary_IsConsistent()
    {
        var timestamp = new UnixTimeUtc(1234567890000L);
        var cursor = new QueryBatchCursor(timestamp);
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithQueryBatchCursorFromJson_IsConsistent()
    {
        var originalCursor = new QueryBatchCursor();
        originalCursor.CursorStartPoint(new UnixTimeUtc(1234567890000L), 123L);
        var json = originalCursor.ToJson();
        var cursor = new QueryBatchCursor(json);

        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithDifferentQueryBatchCursors_ReturnsDifferentHash()
    {
        var cursor1 = new QueryBatchCursor();
        var cursor2 = QueryBatchCursor.FromStartPoint(new UnixTimeUtc(1234567890000L));

        var hash1 = HashParameters.Calculate(cursor1);
        var hash2 = HashParameters.Calculate(cursor2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithQueryBatchCursorSameData_ReturnsSameHash()
    {
        var timestamp = new UnixTimeUtc(1234567890000L);
        var cursor1 = QueryBatchCursor.FromStartPoint(timestamp, 42L);
        var cursor2 = QueryBatchCursor.FromStartPoint(timestamp, 42L);

        var hash1 = HashParameters.Calculate(cursor1);
        var hash2 = HashParameters.Calculate(cursor2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithQueryBatchCursorInvalidJson_IsConsistent()
    {
        var cursor = new QueryBatchCursor("invalid json");
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithIntRange_IsConsistent()
    {
        var range = new IntRange(1, 100);
        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithDifferentIntRanges_ReturnsDifferentHash()
    {
        var range1 = new IntRange(1, 100);
        var range2 = new IntRange(1, 101);

        var hash1 = HashParameters.Calculate(range1);
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithIntRangeSameValues_ReturnsSameHash()
    {
        var range1 = new IntRange(10, 20);
        var range2 = new IntRange(10, 20);

        var hash1 = HashParameters.Calculate(range1);
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithIntRangeSwappedStartEnd_ReturnsDifferentHash()
    {
        var range1 = new IntRange(10, 20);
        var range2 = new IntRange(20, 10);

        var hash1 = HashParameters.Calculate(range1);
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [TestCase(0, 0)]
    [TestCase(-100, 100)]
    [TestCase(int.MinValue, int.MaxValue)]
    [TestCase(42, 42)] // Single value range
    public void Calculate_WithIntRangeEdgeCases_IsConsistent(int start, int end)
    {
        var range = new IntRange(start, end);
        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithInvalidIntRange_IsConsistent()
    {
        var range = new IntRange(100, 1); // Invalid: start > end
        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithIntRangeModifiedAfterCreation_ReturnsDifferentHash()
    {
        var range1 = new IntRange(1, 100);
        var hash1 = HashParameters.Calculate(range1);

        var range2 = new IntRange(1, 100);
        range2.End = 200; // Modify after creation
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcRange_IsConsistent()
    {
        var start = new UnixTimeUtc(1234567890000L);
        var end = new UnixTimeUtc(1234567890000L + 10000L);
        var range = new UnixTimeUtcRange(start, end);
        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithDifferentUnixTimeUtcRanges_ReturnsDifferentHash()
    {
        var start = new UnixTimeUtc(1234567890000L);
        var end1 = new UnixTimeUtc(1234567890000L + 10000L);
        var end2 = new UnixTimeUtc(1234567890000L + 20000L);

        var range1 = new UnixTimeUtcRange(start, end1);
        var range2 = new UnixTimeUtcRange(start, end2);

        var hash1 = HashParameters.Calculate(range1);
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcRangeSameValues_ReturnsSameHash()
    {
        var start = new UnixTimeUtc(1000000000000L);
        var end = new UnixTimeUtc(2000000000000L);
        var range1 = new UnixTimeUtcRange(start, end);
        var range2 = new UnixTimeUtcRange(start, end);

        var hash1 = HashParameters.Calculate(range1);
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcRangeZeroTimes_IsConsistent()
    {
        var start = UnixTimeUtc.ZeroTime;
        var end = new UnixTimeUtc(1000L);
        var range = new UnixTimeUtcRange(start, end);
        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcRangeModifiedAfterCreation_ReturnsDifferentHash()
    {
        var start = new UnixTimeUtc(1000000000000L);
        var end = new UnixTimeUtc(2000000000000L);
        var range1 = new UnixTimeUtcRange(start, end);
        var hash1 = HashParameters.Calculate(range1);

        var range2 = new UnixTimeUtcRange(start, end);
        range2.End = new UnixTimeUtc(3000000000000L); // Modify after creation
        var hash2 = HashParameters.Calculate(range2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [TestCase(0L, 1000L)]
    [TestCase(1234567890000L, 1234567890001L)] // 1ms difference
    [TestCase(-1000L, 0L)] // Before epoch
    public void Calculate_WithUnixTimeUtcRangeVariousValues_IsConsistent(long startMs, long endMs)
    {
        var start = new UnixTimeUtc(startMs);
        var end = new UnixTimeUtc(endMs);
        var range = new UnixTimeUtcRange(start, end);
        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithUnixTimeUtcRangeFromDateTimes_IsConsistent()
    {
        var startTime = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endTime = new DateTime(2023, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var start = UnixTimeUtc.FromDateTime(startTime);
        var end = UnixTimeUtc.FromDateTime(endTime);
        var range = new UnixTimeUtcRange(start, end);

        var hash1 = HashParameters.Calculate(range);
        var hash2 = HashParameters.Calculate(range);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithTimeRowCursor_IsConsistent()
    {
        var time = new UnixTimeUtc(1234567890000L);
        var cursor = new TimeRowCursor(time, 42L);
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithTimeRowCursorNullRowId_IsConsistent()
    {
        var time = new UnixTimeUtc(1234567890000L);
        var cursor = new TimeRowCursor(time, null);
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithDifferentTimeRowCursors_ReturnsDifferentHash()
    {
        var time = new UnixTimeUtc(1234567890000L);
        var cursor1 = new TimeRowCursor(time, 42L);
        var cursor2 = new TimeRowCursor(time, 43L);

        var hash1 = HashParameters.Calculate(cursor1);
        var hash2 = HashParameters.Calculate(cursor2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithTimeRowCursorSameValues_ReturnsSameHash()
    {
        var time = new UnixTimeUtc(1234567890000L);
        var cursor1 = new TimeRowCursor(time, 123L);
        var cursor2 = new TimeRowCursor(time, 123L);

        var hash1 = HashParameters.Calculate(cursor1);
        var hash2 = HashParameters.Calculate(cursor2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithTimeRowCursorDifferentTimes_ReturnsDifferentHash()
    {
        var time1 = new UnixTimeUtc(1234567890000L);
        var time2 = new UnixTimeUtc(1234567890001L);
        var cursor1 = new TimeRowCursor(time1, 42L);
        var cursor2 = new TimeRowCursor(time2, 42L);

        var hash1 = HashParameters.Calculate(cursor1);
        var hash2 = HashParameters.Calculate(cursor2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Calculate_WithTimeRowCursorFromJson_IsConsistent()
    {
        var originalCursor = new TimeRowCursor(new UnixTimeUtc(1234567890000L), 42L);
        var json = originalCursor.ToJson();
        var deserializedCursor = TimeRowCursor.FromJson(json);

        var hash1 = HashParameters.Calculate(originalCursor);
        var hash2 = HashParameters.Calculate(deserializedCursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [TestCase(0L, null)]
    [TestCase(1234567890000L, 0L)]
    [TestCase(-1000L, long.MaxValue)]
    [TestCase(long.MaxValue, long.MinValue)]
    public void Calculate_WithTimeRowCursorVariousValues_IsConsistent(long timeMs, long? rowId)
    {
        var time = new UnixTimeUtc(timeMs);
        var cursor = new TimeRowCursor(time, rowId);
        var hash1 = HashParameters.Calculate(cursor);
        var hash2 = HashParameters.Calculate(cursor);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

}