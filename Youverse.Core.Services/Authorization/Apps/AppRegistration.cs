using System;
using LiteDB;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration() { }

        [BsonId]
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }

        /// <summary>
        /// Defines the permission set this app perform, even if the caller has access (this includes the owner).
        /// </summary>
        public PermissionSet NegatedPermissionSet { get; set; }
        
        /// <summary>
        /// The exchange grant tied to this app, which gives the app its drive access and permissions.
        /// </summary>
        public Guid ExchangeGrantId { get; set; }

    }
}