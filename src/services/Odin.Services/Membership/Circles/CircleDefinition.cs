using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles
{
    public class CircleDefinition : IEquatable<CircleDefinition>
    {
        public GuidId Id { get; set; }

        public Int64 Created { get; set; }

        public Int64 LastUpdated { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool Disabled { get; set; }

        /// <summary>
        /// The drives granted to members of this Circle
        /// </summary>
        public IEnumerable<DriveGrantRequest> DriveGrants { get; set; }

        /// <summary>
        /// The permissions to be granted to members of this Circle
        /// </summary>
        public PermissionSet Permissions { get; set; }

        public bool Equals(CircleDefinition other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Id, other.Id) && Created == other.Created && LastUpdated == other.LastUpdated && Name == other.Name &&
                   Description == other.Description && Disabled == other.Disabled && MatchDriveGrants(other.DriveGrants.ToList()) &&
                   Equals(Permissions, other.Permissions);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((CircleDefinition)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Created, LastUpdated, Name, Description, Disabled, DriveGrants, Permissions);
        }
        
        private bool MatchDriveGrants(List<DriveGrantRequest> otherDriveGrants)
        {
            return !DriveGrants.Except(otherDriveGrants).Any() &&
                   !otherDriveGrants.Except(DriveGrants).Any();
        }
    }
}