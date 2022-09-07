using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dawn;

namespace Youverse.Core.Services.Authorization.Permissions
{
    public class PermissionSet : IEquatable<PermissionSet>
    {
        public List<string> Keys { get; init; }

        public PermissionSet()
        {
        }

        public PermissionSet(IEnumerable<string> permissionKeys)
        {
            Guard.Argument(permissionKeys, nameof(permissionKeys)).NotNull().NotEmpty();
            // Keys = new ReadOnlyCollection<string>(permissionKeys.Select(p => p.ToLower()).ToList());
            Keys = new List<string>(permissionKeys.Select(p => p.ToLower()).ToList());
        }

        public bool HasKey(string key)
        {
            return Keys.Contains(key.ToLower());
        }

        public bool Equals(PermissionSet other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Keys, other.Keys);
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
            return (Keys.GetHashCode());
        }

        public static bool operator ==(PermissionSet p1, PermissionSet p2)
        {
            if (p1 is null)
            {
                return p2 is null;
            }

            var p1Keys = p1.Keys ?? new List<string>();
            var p2Keys = p2.Keys ?? new List<string>();

            return p1Keys.SequenceEqual(p2Keys);
        }

        public static bool operator !=(PermissionSet p1, PermissionSet p2)
        {
            return !(p1 == p2);
        }
    }
}