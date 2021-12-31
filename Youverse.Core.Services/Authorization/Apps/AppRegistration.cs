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

        public SymmetricKeyEncryptedAes EncryptedAppDek { get; set; }

        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// The drive associated with this app.
        /// </summary>
        public Guid? DriveId { get; set; }
    }
}