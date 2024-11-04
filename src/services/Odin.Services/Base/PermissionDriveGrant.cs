using System.Collections.Generic;
using System.Linq;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Base
{
    /// <summary>
    /// Indicates a set of permissions being requested
    /// </summary>
    public class PermissionSetGrantRequest
    {
        /// <summary>
        /// The drives being requested
        /// </summary>
        public IEnumerable<DriveGrantRequest> Drives { get; set; }

        /// <summary>
        /// The permissions being requested
        /// </summary>
        public PermissionSet PermissionSet { get; set; }
    }
}