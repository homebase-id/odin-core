using System;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;

namespace Odin.Services.Authorization.ExchangeGrants
{
    public class DriveGrant : IGenericCloneable<DriveGrant>
    {
        /// <summary>
        /// The internal drive id being granted access
        /// </summary>
        public Guid DriveId { get; set; }
        public PermissionedDrive PermissionedDrive { get; set; }
        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }

        public DriveGrant Clone()
        {
            return new DriveGrant
            {
                DriveId = DriveId,
                PermissionedDrive = PermissionedDrive.Clone(),
                KeyStoreKeyEncryptedStorageKey = KeyStoreKeyEncryptedStorageKey?.Clone()
            };
        }

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

        public override string ToString()
        {
            return $"Drive:{PermissionedDrive.Drive} is granted [{PermissionedDrive.Permission}] and HasStorageKey:{HasStorageKey}";
        }
    }
}