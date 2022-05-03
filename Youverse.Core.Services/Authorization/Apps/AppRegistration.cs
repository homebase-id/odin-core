using System;
using LiteDB;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public AppRegistration()
        {
        }

        [BsonId]
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }

        /// <summary>
        /// The value use used to decrypt storage keys within DriveGrants
        /// </summary>
        [Obsolete]
        public SymmetricKeyEncryptedAes MasterKeyEncryptedAppKey { get; set; }

        [Obsolete]
        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// The exchange grant tied to this app, which gives the app its drive access and permissions.
        /// </summary>
        public Guid ExchangeGrantId { get; set; }

    }
}