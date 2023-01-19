using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Indicates a set of permissions being requested
    /// </summary>
    public class PermissionSetGrantRequest
    {
        public List<DriveGrantRequest> Drives { get; set; }
        public PermissionSet PermissionSet { get; set; }
    }
}