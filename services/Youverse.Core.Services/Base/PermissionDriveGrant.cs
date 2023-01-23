using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Base
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