using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class PermissionDriveGrant
    {
        public Guid DriveId { get; set; }
        
        public SymmetricKeyEncryptedAes EncryptedStorageKey { get; set; }
        
        /// <summary>
        /// The type of access allowed for this drive grant
        /// </summary>
        public DrivePermissions Permissions { get; set; }
        
    }
}