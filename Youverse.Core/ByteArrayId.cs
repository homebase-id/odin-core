using System;
using System.Linq;
using System.Text.Json.Serialization;
using Dawn;
using Youverse.Core.Exceptions;

namespace Youverse.Core;

[JsonConverter(typeof(ByteArrayIdConverter))]
public class ByteArrayId
{
    public ByteArrayId()
    {
        //for litedb.  remove when litedb is removed
    }

    public byte[] Value { get; init; }

    public ByteArrayId(string value64) : this(Convert.FromBase64String(value64))
    {
    }

    public ByteArrayId(byte[] id)
    {
        AssertIsValid(id);
        Value = id;
    }

    public override string ToString()
    {
        if (this.Value == null)
        {
            return "";
        }

        return Convert.ToBase64String(this.Value);
    }

    public static bool operator ==(ByteArrayId b1, ByteArrayId b2)
    {
        if (ReferenceEquals(b1, b2))
        {
            return true;
        }

        var arr1 = b1?.Value ?? Array.Empty<byte>();
        var arr2 = b2?.Value ?? Array.Empty<byte>();

        return arr1.SequenceEqual(arr2);
    }

    public static bool operator ==(Guid g1, ByteArrayId b1)
    {
        var b2 = g1.ToByteArray();
        var arr1 = b1?.Value ?? Array.Empty<byte>();
        return arr1.SequenceEqual(b2);
    }

    public static bool operator !=(Guid g1, ByteArrayId b1)
    {
        return !(b1 == g1);
    }

    public static bool operator ==(ByteArrayId b1, Guid g1)
    {
        var b2 = g1.ToByteArray();
        var arr1 = b1?.Value ?? Array.Empty<byte>();
        return arr1.SequenceEqual(b2);
    }

    public static bool operator !=(ByteArrayId b1, Guid b2)
    {
        return !(b1 == b2);
    }

    public static bool operator !=(ByteArrayId b1, ByteArrayId b2)
    {
        return !(b1 == b2);
    }

    public static implicit operator byte[](ByteArrayId id)
    {
        return id.Value;
    }

    public static explicit operator ByteArrayId(byte[] id)
    {
        return new ByteArrayId(id);
    }

    public static implicit operator Guid(ByteArrayId id)
    {
        if (CanConvertToGuid(id))
        {
            return new Guid(id.Value);
        }

        throw new YouverseException("Value cannot be converted to a Guid; Check your length");
    }

    public static implicit operator ByteArrayId(Guid id)
    {
        return new ByteArrayId(id.ToByteArray());
    }

    public static ByteArrayId Empty => new ByteArrayId(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

    public static ByteArrayId NewId()
    {
        return new ByteArrayId(Guid.NewGuid().ToByteArray());
    }

    public static bool CanConvertToGuid(ByteArrayId id)
    {
        return id.Value.Length == 16;
    }

    public static bool IsValid(byte[] id)
    {
        if (null == id)
        {
            return false;
        }

        bool HasValidLength(byte[] v) => v.Length is >= 8 and <= 16;

        // bool HasValidChars(byte[] v) => v.Any(b => b is <(byte)1 and > (byte)127);
        bool HasValidChars(byte[] v) => true;

        if (HasValidLength(id) && HasValidChars(id))
        {
            return true;
        }

        return false;
    }

    public static void AssertIsValid(byte[] id)
    {
        if (!IsValid(id))
        {
            throw new YouverseException("Invalid id");
        }
    }

    public static ByteArrayId FromString(string input)
    {
        Guard.Argument(input, nameof(input)).NotEmpty().NotNull("Invalid input");
        return new ByteArrayId(input.ToLower().ToUtf8ByteArray());
    }
}