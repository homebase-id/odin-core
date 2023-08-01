using System.Collections.Generic;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth
{
    public class YouAuthDomainRegistrationRequest
    {
        public string Domain { get; set; }

        public string Name { get; set; }
        
        /// <summary>
        /// The host name used for CORS to allow the app to access the identity from a browser
        /// </summary>
        public string CorsHostName { get; set; }

        /// <summary>
        /// Permissions to be granted to this domain
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        /// <summary>
        /// The list of drives of which this domain should receive access
        /// </summary>
        public List<DriveGrantRequest> Drives { get; set; }
    }
}