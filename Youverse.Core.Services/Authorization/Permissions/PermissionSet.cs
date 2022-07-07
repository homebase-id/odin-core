using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Permissions
{
    public class PermissionSet:IEquatable<PermissionSet>
    {
        public PermissionSet()
        {
        }

        public Dictionary<SystemApi, int> Permissions { get; } = new Dictionary<SystemApi, int>();
        
        public bool Equals(PermissionSet other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Permissions, other.Permissions);
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
            return (Permissions != null ? Permissions.GetHashCode() : 0);
        }
        
        public static bool operator ==(PermissionSet p1, PermissionSet p2)
        {
            if (p1 is null)
            {
                return p2 is null;
            }
            
            var diffs = p1.Permissions.Except(p2.Permissions);

            return !diffs.Any();
        }

        public static bool operator !=(PermissionSet p1, PermissionSet p2)
        {
            return !(p1 == p2);
        }
    }
}