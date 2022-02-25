using System;
using System.Collections.Generic;
using LiteDB;
using Youverse.Core.Cryptography;
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
        public SymmetricKeyEncryptedAes MasterKeyEncryptedAppKey { get; set; }

        public bool IsRevoked { get; set; }

        /// <summary>
        /// The default drive to be used with this app.
        /// </summary>
        public Guid? DefaultDriveId { get; set; }
        
        /// <summary>
        /// All drives owned by this app
        /// </summary>
        public List<AppDriveGrant> OwnedDrives { get; set; }
        
        
        /// <summary>
        /// List of additional drives to which this app has access.  The key is the DriveId.  The value is the is the Drive's storage DEK 
        /// </summary>
        public List<AppDriveGrant> AdditionalDriveGrants { get; set; }

        /// <summary>
        /// Indicates this app is allowed to manage connections, including sending, accepting, and removing requests and existing connections
        /// </summary>
        public bool CanManageConnections { get; set; }
    }
}