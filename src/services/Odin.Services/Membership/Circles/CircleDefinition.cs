using System;
using System.Collections.Generic;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Membership.Circles
{
    public class CircleDefinition
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
    }
}