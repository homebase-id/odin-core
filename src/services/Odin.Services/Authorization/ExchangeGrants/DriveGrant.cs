using System;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Services.Drives;

namespace Odin.Services.Authorization.ExchangeGrants
{
    public class DriveGrant : IGenericCloneable<DriveGrant>
    {
        public Guid DriveId { get; set; }
        public PermissionedDrive PermissionedDrive { get; set; }

        public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedStorageKey { get; set; }

        /// <summary>
        /// Read with the storage key: what it takes to decrypt the drive's content, not merely list it.
        /// </summary>
        public bool IsKeyedRead => PermissionedDrive.Permission.HasFlag(DrivePermission.Read) &&
                                   KeyStoreKeyEncryptedStorageKey != null;

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