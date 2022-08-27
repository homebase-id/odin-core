using System;

namespace Youverse.Core.Services.Authorization.Permissions
{
    public class PermissionSet : IEquatable<PermissionSet>
    {
        public PermissionSet(PermissionFlags permissionFlags)
        {
            PermissionFlags = permissionFlags;
        }
        public PermissionFlags PermissionFlags { get; }
        
        public bool Equals(PermissionSet other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(PermissionFlags, other.PermissionFlags);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PermissionSet)obj);
        }

        public override int GetHashCode()
        {
            return (PermissionFlags.GetHashCode());
        }

        public static bool operator ==(PermissionSet p1, PermissionSet p2)
        {
            if (p1 is null)
            {
                return p2 is null;
            }

            return p1.PermissionFlags == p2?.PermissionFlags;
        }

        public static bool operator !=(PermissionSet p1, PermissionSet p2)
        {
            return !(p1 == p2);
        }
    }
}