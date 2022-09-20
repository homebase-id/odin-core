using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Drive;

/// <summary>
///  A drive specifier for incoming requests to perform actions on a drive.  (essentially, this hides the internal DriveId).
/// </summary>
[DebuggerDisplay("Alias={Alias.ToBase64()} Type={Type.ToBase64()}")]
public class TargetDrive : IEquatable<TargetDrive>
{
    public GuidId Alias { get; set; }
    public GuidId Type { get; set; }

    public byte[] ToKey()
    {
        return ByteArrayUtil.Combine(Type, Alias);
    }

    public bool IsValid()
    {
        return GuidId.IsValid(this.Alias) && GuidId.IsValid(this.Type);
    }

    public static TargetDrive NewTargetDrive()
    {
        return new TargetDrive()
        {
            Alias = (GuidId)ByteArrayUtil.GetRndByteArray(8),
            Type = (GuidId)ByteArrayUtil.GetRndByteArray(8)
        };
    }

    public static bool operator ==(TargetDrive d1, TargetDrive d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return true;
        }

        var d1Key = d1?.ToKey() ?? Array.Empty<byte>();

        return d1Key.SequenceEqual(d2?.ToKey() ?? Array.Empty<byte>());
    }

    public static bool operator !=(TargetDrive d1, TargetDrive d2)
    {
        return !(d1 == d2);
    }

    public bool Equals(TargetDrive other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return (Alias == other.Alias) && (Type == other.Type);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((TargetDrive)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Alias, Type);
    }
}