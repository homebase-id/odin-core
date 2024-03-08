using System;
using Odin.Core.Cryptography.Data;

namespace Odin.Services.Authorization.ExchangeGrants
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
                HasStorageKey = KeyStoreKeyEncryptedStorageKey != null,
                PermissionedDrive = this.PermissionedDrive
            };
        }
    }

    public class RedactedDriveGrant
    {
        public PermissionedDrive PermissionedDrive { get; set; }
        public bool HasStorageKey { get; set; }
    }
}