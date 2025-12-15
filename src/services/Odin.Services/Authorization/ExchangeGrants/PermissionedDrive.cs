using System;
using Odin.Core.Serialization;
using Odin.Services.Drives;

namespace Odin.Services.Authorization.ExchangeGrants;

/// <summary>
/// Basis for a drive which has been assigned a permission, even if it has not been granted
/// </summary>
public class PermissionedDrive : IEquatable<PermissionedDrive>, IGenericCloneable<PermissionedDrive>
{
    /// <summary>
    /// The drive being granted the permission.
    /// </summary>
    public TargetDrive Drive { get; set; }

    /// <summary>
    /// The type of access allowed for this drive grant
    /// </summary>
    public DrivePermission Permission { get; set; }

    public PermissionedDrive Clone()
    {
        return new PermissionedDrive
        {
            Drive = Drive.Clone(),
            Permission = Permission
        };
    }

    public static bool operator ==(PermissionedDrive pd1, PermissionedDrive pd2)
    {
        if (ReferenceEquals(pd1, pd2))
        {
            return true;
        }

        return pd1?.Drive == pd2?.Drive && pd1?.Permission == pd2?.Permission;
    }

    public static bool operator !=(PermissionedDrive pd1, PermissionedDrive pd2)
    {
        return !(pd1 == pd2);
    }

    public bool Equals(PermissionedDrive other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Drive == other.Drive;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((PermissionedDrive)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Drive, (int)Permission);
    }
    
    public override string ToString()
    {
        var alias = Drive?.Alias;
        // var type = Drive?.Type?.ToString() ?? "<no-type>";
        return $"PermissionedDrive [Alias={alias}, Permission={Permission}]";
    }
}