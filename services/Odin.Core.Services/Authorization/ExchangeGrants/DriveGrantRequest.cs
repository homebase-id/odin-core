using System;

namespace Odin.Core.Services.Authorization.ExchangeGrants;

/// <summary>
/// A request to get access to a drive along with a permission
/// </summary>
public class DriveGrantRequest : IEquatable<DriveGrantRequest>
{
    public PermissionedDrive PermissionedDrive { get; set; }

    public static bool operator ==(DriveGrantRequest d1, DriveGrantRequest d2)
    {
        if (ReferenceEquals(d1, d2))
        {
            return true;
        }

        return d1?.PermissionedDrive == d2?.PermissionedDrive;
    }

    public static bool operator !=(DriveGrantRequest d1, DriveGrantRequest d2)
    {
        return !(d1 == d2);
    }
    
    public bool Equals(DriveGrantRequest other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return PermissionedDrive == other.PermissionedDrive;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((DriveGrantRequest)obj);
    }

    public override int GetHashCode()
    {
        return (PermissionedDrive != null ? PermissionedDrive.GetHashCode() : 0);
    }
}