using System;
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Services.Base;
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

        /// <summary>
        /// Takes the storage key off each of <paramref name="grants"/> on <paramref name="driveId"/>, keeping the
        /// permission; whether any had one.  For a drive gone zero knowledge that kept its key
        /// (docs/zero-knowledge-drive-api-plan.md, 3.3).
        /// </summary>
        public static bool RemoveStorageKey(IEnumerable<DriveGrant> grants, Guid driveId)
        {
            var removed = false;
            foreach (var grant in grants ?? [])
            {
                if (grant.DriveId == driveId && grant.KeyStoreKeyEncryptedStorageKey != null)
                {
                    grant.KeyStoreKeyEncryptedStorageKey = null;
                    removed = true;
                }
            }

            return removed;
        }

        public DriveGrant Clone()
        {
            return new DriveGrant
            {
                DriveId = DriveId,
                PermissionedDrive = PermissionedDrive.Clone(),
                KeyStoreKeyEncryptedStorageKey = KeyStoreKeyEncryptedStorageKey?.Clone()
            };
        }

        /// <param name="zkDriveKeys">An app's locked keys to its zero-knowledge drives (<see cref="OdinClientContext.ZkDriveKeys"/>),
        /// shown on this grant when they include its drive</param>
        public RedactedDriveGrant Redacted(IReadOnlyDictionary<Guid, ZkAppDriveKey> zkDriveKeys = null)
        {
            var zk = zkDriveKeys?.GetValueOrDefault(DriveId);
            return new RedactedDriveGrant()
            {
                HasStorageKey = KeyStoreKeyEncryptedStorageKey != null,
                PermissionedDrive = this.PermissionedDrive,
                ZkAppKeyEncryptedStorageKey = zk?.ZkAppKeyEncryptedStorageKey,
                ZkStorageKeyEncryptedWriteOnlyPrivateKey = zk?.ZkStorageKeyEncryptedWriteOnlyPrivateKey
            };
        }
    }

    public class RedactedDriveGrant
    {
        public PermissionedDrive PermissionedDrive { get; set; }
        public bool HasStorageKey { get; set; }

        /// <summary>
        /// On a zero-knowledge drive the app is granted: the drive key locked with the app's zero-knowledge key,
        /// for the app's devices to open (docs/zero-knowledge-drive-api-plan.md, step 3, 3.5).  Null otherwise.
        /// </summary>
        public string ZkAppKeyEncryptedStorageKey { get; set; }

        /// <summary>With <see cref="ZkAppKeyEncryptedStorageKey"/>: the drive's write-only private key, locked with the drive key.</summary>
        public string ZkStorageKeyEncryptedWriteOnlyPrivateKey { get; set; }

        public override string ToString()
        {
            return $"Drive:{PermissionedDrive.Drive} is granted [{PermissionedDrive.Permission}] and HasStorageKey:{HasStorageKey}";
        }
    }
}