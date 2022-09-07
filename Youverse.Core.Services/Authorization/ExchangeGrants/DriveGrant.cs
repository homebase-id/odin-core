using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class DriveGrant : PermissionedDrive
    {
        /// <summary>
        /// The internal drive id being granted access
        /// </summary>
        public Guid DriveId { get; set; }
        
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }
        
        public RedactedDriveGrant Redacted()
        {
            return new RedactedDriveGrant()
            {
                Drive = this.Drive,
                Permission = this.Permission
            };
        }
    }

    public class RedactedDriveGrant
    {
        public TargetDrive Drive { get; set; }
        public DrivePermission Permission { get; set; }
    }
}