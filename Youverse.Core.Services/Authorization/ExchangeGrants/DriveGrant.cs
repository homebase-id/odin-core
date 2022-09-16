using System;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    public class DriveGrant
    {
        /// <summary>
        /// The internal drive id being granted access
        /// </summary>
        public Guid DriveId { get; set; }

        public PermissionedDrive PermissionedDrive { get; set; }
        
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }

        public RedactedDriveGrant Redacted()
        {
            return new RedactedDriveGrant()
            {
                PermissionedDrive = this.PermissionedDrive
            };
        }
    }

    public class RedactedDriveGrant
    {
        public PermissionedDrive PermissionedDrive { get; set; }

    }
}