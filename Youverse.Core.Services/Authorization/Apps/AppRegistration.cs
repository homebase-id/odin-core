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
        /// The value use used to access storage keys
        /// </summary>
        public SensitiveByteArray EncryptionKey { get; set; }

        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// The drive associated with this app.
        /// </summary>
        public Guid? DriveId { get; set; }
        
        /// <summary>
        /// List of additional drives to which this app has access.  The key is the DriveId.  The value is the is the Drive's storage DEK 
        /// </summary>
        public Dictionary<Guid, SensitiveByteArray> DriveGrants { get; set; }
    }
}