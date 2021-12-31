using System;
using LiteDB;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        [BsonId]
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }

        /// <summary>
        /// The value use used to access storage keys
        /// </summary>
        public SymmetricKeyEncryptedAes EncryptionKek { get; set; }

        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// The drive associated with this app.
        /// </summary>
        public Guid? DriveId { get; set; }
    }

    /// <summary>
    /// Grants an app access to a drive
    /// </summary>
    public class AppDriveAccess
    {
        public Guid AppId { get; set; }

    }
}