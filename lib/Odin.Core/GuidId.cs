using System;
using System.Text.Json.Serialization;
using Dawn;
using Odin.Core.Exceptions;
using Odin.Core.Util;

namespace Odin.Core;

[JsonConverter(typeof(GuidIdConverter))]
public class GuidId
{
    // public byte[] Value { get; init; }
    public Guid Value { get; init; }

    protected bool Equals(GuidId other)
    {
        return Equals(Value, other.Value);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((GuidId)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (byte element in Value.ToByteArray())
            {
                hash = hash * 31 + element.GetHashCode();
            }

            return hash;
        }
    }

    public GuidId()
    {
    }

    public GuidId(string value)
    {
        try
        {
            this.Value = new Guid(value);
        }
        catch
        {
            this.Value = new Guid(Convert.FromBase64String(value));
        }
    }

    public GuidId(Guid value)
    {
        Value = value;
    }

    public GuidId(byte[] id)
    {
        AssertIsValid(id);
        Value = new Guid(id);
    }

    public override string ToString()
    {
        return this.Value.ToString("N");
    }

    public static bool operator ==(GuidId b1, GuidId b2)
    {
        if (ReferenceEquals(b1, b2))
        {
            return true;
        }

        return b1?.Value == b2?.Value;
    }

    public static bool operator ==(Guid g1, GuidId b1)
    {
        return g1 == b1?.Value;
    }

    public static bool operator !=(Guid g1, GuidId b1)
    {
        return !(b1 == g1);
    }

    public static bool operator ==(GuidId b1, Guid g1)
    {
        return b1?.Value == g1;
    }

    public static bool operator !=(GuidId b1, Guid b2)
    {
        return !(b1 == b2);
    }

    public static bool operator !=(GuidId b1, GuidId b2)
    {
        return !(b1 == b2);
    }

    public static implicit operator byte[](GuidId id)
    {
        return id.Value.ToByteArray();
    }

    public static explicit operator GuidId(byte[] id)
    {
        return new GuidId(id);
    }

    public static implicit operator Guid(GuidId id)
    {
        return id.Value;
    }

    public static implicit operator GuidId(Guid id)
    {
        return new GuidId(id.ToByteArray());
    }

    public static GuidId Empty => new GuidId(Guid.Empty.ToByteArray());

    public static GuidId NewId()
    {
        return new GuidId(Guid.NewGuid().ToByteArray());
    }

    public static bool IsValid(byte[] id)
    {
        if (null == id)
        {
            return false;
        }

        try
        {
            //TODO: this is a in-efficient way to see if this is valid; consider when performance testing
            var _ = new Guid(id);
        }
        catch (Exception)
        {
            return false;
        }

        return true;
    }

    public static void AssertIsValid(byte[] id)
    {
        if (!IsValid(id))
        {
            throw new YouverseClientException("Invalid id", YouverseClientErrorCode.UnknownId);
        }
    }

    public static GuidId FromString(string input, bool toLower = true)
    {
        Guard.Argument(input, nameof(input)).NotEmpty().NotNull("Invalid input");
        var guid = HashUtil.ReduceSHA256Hash(toLower ? input.ToLower() : input);
        return new GuidId(guid);
    }

    public string ToBase64()
    {
        return this.Value.ToByteArray().ToBase64();
    }
}
