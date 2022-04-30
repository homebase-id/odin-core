using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    
    //TODO: this is now a duplciate of DriveGrant.  need to remove this when we refactor everyting into the ExchangeDriveGrant
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