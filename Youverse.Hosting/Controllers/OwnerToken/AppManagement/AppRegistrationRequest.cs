using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Contacts.Circle;


namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    public class AppRegistrationRequest
    {
        public Guid AppId { get; set; } 
        
        public string Name { get; set; }
        
        /// <summary>
        /// Permissions to be granted to this app
        /// </summary>
        public PermissionSet PermissionSet { get; set; }
        
        /// <summary>
        /// The list of drives of which this app should receive access
        /// </summary>
        public List<DriveGrantRequest> Drives { get; set; }
    }
}