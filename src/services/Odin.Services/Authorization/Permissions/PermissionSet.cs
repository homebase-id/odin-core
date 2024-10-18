using System;
using System.Collections.Generic;
using System.Linq;


namespace Odin.Services.Authorization.Permissions
{
    public class PermissionSet : IEquatable<PermissionSet>
    {
        public List<int> Keys { get; init; }

        public PermissionSet()
        {
            Keys = new List<int>();
        }

        public PermissionSet(params int[] permissionKeys) : this(new List<int>(permissionKeys))
        {
        }

        public PermissionSet(IEnumerable<int> permissionKeys)
        {
            var pk = permissionKeys as int[] ?? permissionKeys.ToArray();
            Keys = new List<int>(pk);
        }

        public PermissionSet(PermissionSet other)
        {
            Keys = other.Keys.ToList();
        }

        public PermissionSet Clone()
        {
            return new PermissionSet(this);
        }

        public bool HasKey(int key)
        {
            return Keys?.Any(k => k == key) ?? false;
        }

        public bool Equals(PermissionSet other)
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

            var p1Keys = p1.Keys?.OrderBy(x => x).ToList() ?? new List<int>();
            var p2Keys = p2?.Keys?.OrderBy(x => x).ToList() ?? new List<int>();

            return p1Keys.SequenceEqual(p2Keys);
        }

        public static bool operator !=(PermissionSet p1, PermissionSet p2)
        {
            return !(p1 == p2);
        }

        public RedactedPermissionSet Redacted()
        {
            return new RedactedPermissionSet()
            {
                Keys = this.Keys
            };
        }
    }

    public class RedactedPermissionSet
    {
        public List<int> Keys { get; set; } = new();
    }
}