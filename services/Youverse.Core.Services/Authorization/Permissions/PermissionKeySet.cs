using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dawn;

namespace Youverse.Core.Services.Authorization.Permissions
{
    public class PermissionKeySet : IEquatable<PermissionKeySet>
    {
        public List<int> Keys { get; init; }

        public PermissionKeySet()
        {
        }

        public PermissionKeySet(IEnumerable<int> permissionKeys)
        {
            Guard.Argument(permissionKeys, nameof(permissionKeys)).NotNull();
            Keys = new List<int>(permissionKeys.ToList());
        }

        public bool HasKey(int key)
        {
            return Keys?.Any(k => k == key) ?? false;
        }

        public bool Equals(PermissionKeySet other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return this == other;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PermissionKeySet)obj);
        }

        public override int GetHashCode()
        {
            return (Keys.GetHashCode());
        }

        public static bool operator ==(PermissionKeySet p1, PermissionKeySet p2)
        {
            if (p1 is null)
            {
                return p2 is null;
            }

            var p1Keys = p1.Keys?.OrderBy(x => x).ToList() ?? new List<int>();
            var p2Keys = p2?.Keys?.OrderBy(x => x).ToList() ?? new List<int>();

            return p1Keys.SequenceEqual(p2Keys);
        }

        public static bool operator !=(PermissionKeySet p1, PermissionKeySet p2)
        {
            return !(p1 == p2);
        }
    }
}