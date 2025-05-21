using System;
using System.Diagnostics;
using System.Linq;
using Odin.Core.Serialization;

namespace Odin.Core.Storage.SQLite.Migrations.DriveAliasToId;

/// <summary>
///  A drive specifier for incoming requests to perform actions on a drive.  (essentially, this hides the internal DriveId).
/// </summary>
[DebuggerDisplay("{ToString()}")]
public class TargetDriveForMigration : IEquatable<TargetDriveForMigration>, IGenericCloneable<TargetDriveForMigration>
{
    public GuidId Alias { get; set; }
    public GuidId Type { get; set; }

    public TargetDriveForMigration Clone()
    {
        return new TargetDriveForMigration
        {
            Alias = Alias.Clone(),
            Type = Type.Clone()
        };
    }

    public byte[] ToKey()
    {
        return ByteArrayUtil.Combine(Type, Alias);
    }

    public bool IsValid()
    {
        return GuidId.IsValid(this.Alias) && GuidId.IsValid(this.Type);
    }

    public static TargetDriveForMigration NewTargetDriveForMigration()
    {
        return new TargetDriveForMigration()
        {
            Alias = Guid.NewGuid(),
            Type = Guid.NewGuid()
        };
    }
    
    public static TargetDriveForMigration NewTargetDriveForMigration(Guid type)
    {
        return new TargetDriveForMigration()
        {
            Alias = Guid.NewGuid(),
            Type = type
        };
    }


    public static bool operator ==(TargetDriveForMigration d1, TargetDriveForMigration d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return true;
        }

        var d1Key = d1?.ToKey() ?? Array.Empty<byte>();

        return d1Key.SequenceEqual(d2?.ToKey() ?? Array.Empty<byte>());
    }

    public static bool operator !=(TargetDriveForMigration d1, TargetDriveForMigration d2)
    {
        return !(d1 == d2);
    }

    public bool Equals(TargetDriveForMigration other)
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
        return Equals((TargetDriveForMigration)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Alias, Type);
    }

    public override string ToString()
    {
        return $"Alias={Alias} Type={Type}";
    }
}