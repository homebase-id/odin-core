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

    /// <summary>
    /// Optional per-grant override (in seconds) for the <see cref="DrivePermission.ConditionalTemporalRead"/>
    /// lookback window. When set, the effective window is the smaller of this value and the drive-level
    /// ceiling (see <see cref="Odin.Services.Drives.TemporalRead"/>). Null falls back to the default window.
    /// Ignored for non-temporal grants.
    /// </summary>
    public long? TemporalReadWindowSeconds { get; set; }

    public PermissionedDrive Clone()
    {
        return new PermissionedDrive
        {
            Drive = Drive.Clone(),
            Permission = Permission,
            TemporalReadWindowSeconds = TemporalReadWindowSeconds
        };
    }

    public static bool operator ==(PermissionedDrive pd1, PermissionedDrive pd2)
    {
        if (ReferenceEquals(pd1, pd2))
        {
            return true;
        }

        return pd1?.Drive == pd2?.Drive && pd1?.Permission == pd2?.Permission &&
               pd1?.TemporalReadWindowSeconds == pd2?.TemporalReadWindowSeconds;
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
        return HashCode.Combine(Drive, (int)Permission, TemporalReadWindowSeconds);
    }
    
    public override string ToString()
    {
        var alias = Drive?.Alias;
        var type = Drive?.Type?.ToString() ?? "<no-type>";
        return $"PermissionedDrive [Alias={alias}, Type={type}, Permission={Permission}]";
    }
}