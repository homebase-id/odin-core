using System;
using LiteDB;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration() { }

        [BsonId]
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }

        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// The exchange grant tied to this app, which gives the app its drive access and permissions.
        /// </summary>
        public Guid ExchangeGrantId { get; set; }

    }
}