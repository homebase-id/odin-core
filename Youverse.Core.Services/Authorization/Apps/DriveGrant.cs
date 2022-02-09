using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class DriveGrant
    {
        public Guid DriveId { get; set; }
        public SymmetricKeyEncryptedAes AppKeyEncryptedStorageKey { get; set; }
        
        /// <summary>
        /// The type of access allowed for this drive grant
        /// </summary>
        public DrivePermissions Permissions { get; set; }
        
    }
}