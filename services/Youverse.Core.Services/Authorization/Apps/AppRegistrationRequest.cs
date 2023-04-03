using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationRequest
    {
        public AppRegistrationRequest()
        {
            
        }
        
        public GuidId AppId { get; set; }

        public string Name { get; set; }
        
        /// <summary>
        /// The host name used for CORS to allow the app to access the identity from a browser
        /// </summary>
        public string CorsHostName { get; set; }

        /// <summary>
        /// Permissions to be granted to this app
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        /// <summary>
        /// The list of drives of which this app should receive access
        /// </summary>
        public List<DriveGrantRequest> Drives { get; set; }

        /// <summary>
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }
        
        /// <summary>
        /// Permissions being granted to allmembers of the <see cref="AuthorizedCircles"/>
        /// </summary>
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }

    }
    
    public class UpdateAuthorizedCirclesRequest
    {
        public GuidId AppId { get; set; }

        /// <summary>
        /// List of circles defining whose members can work with your identity via this app
        /// </summary>
        public List<Guid> AuthorizedCircles { get; set; }

        /// <summary>
        /// Permissions granted to members of the <see cref="AuthorizedCircles"/>
        /// </summary>
        public PermissionSetGrantRequest CircleMemberPermissionGrant { get; set; }
    }
    
    public class UpdateAppPermissionsRequest
    {
        public GuidId AppId { get; set; }

        /// <summary>
        /// Permissions to be granted to this app
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        /// <summary>
        /// The list of drives of which this app should receive access
        /// </summary>
        public IEnumerable<DriveGrantRequest> Drives { get; set; }

    }
}