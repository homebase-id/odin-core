using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Util;
using Serilog;

namespace Odin.Services.Base
{
    [DebuggerDisplay("{Stats}")]
    public class PermissionContext : IGenericCloneable<PermissionContext>
    {
        private readonly bool _isSystem = false;
        public SensitiveByteArray SharedSecretKey { get; private set; }
        internal Dictionary<string, PermissionGroup> PermissionGroups { get; }

        internal string Stats
        {
            get
            {
                var c1 = this.PermissionGroups.Keys.Count;
                var c2 = this.PermissionGroups.Values.Sum(g => g.DriveGrantCount);
                return $"{c2} drive grants across {c1} permission groups";
            }
        }

        public PermissionContext(
            Dictionary<string, PermissionGroup> permissionGroups,
            SensitiveByteArray sharedSecretKey,
            bool isSystem = false)
        {
            SharedSecretKey = sharedSecretKey;
            PermissionGroups = permissionGroups ?? new Dictionary<string, PermissionGroup>();
            _isSystem = isSystem;
        }

        public PermissionContext(PermissionContext other)
        {
            _isSystem = other._isSystem;
            SharedSecretKey = other.SharedSecretKey?.Clone();
            PermissionGroups = new Dictionary<string, PermissionGroup>();
            foreach (var (key, value) in other.PermissionGroups)
            {
                PermissionGroups[key] = value.Clone();
            }
        }

        public PermissionContext Clone()
        {
            return new PermissionContext(this);
        }

        public void SetSharedSecretKey(SensitiveByteArray value)
        {
            this.SharedSecretKey = value;
        }

        public SensitiveByteArray DecryptUsingKeyStoreKey(SymmetricKeyEncryptedAes encryptedKeyStoreKey)
        {
            // TODO: need to move the key store key storage to this
            // upper class rather than having to hunt thru the permission groups
            
            var groupWithKey = PermissionGroups.Values.FirstOrDefault(group => group.GetKeyStoreKey()?.IsSet() ?? false);

            if (null == groupWithKey)
            {
                throw new OdinSecurityException($"No key store key found");
            }
            
            return encryptedKeyStoreKey.DecryptKeyClone(groupWithKey.GetKeyStoreKey());
        }
        public SensitiveByteArray GetKeyStoreKey()
        {
            // TODO: need to move the key store key storage to this
            // upper class rather than having to hunt thru the permission groups
            
            var groupWithKey = PermissionGroups.Values.FirstOrDefault(group => group.GetKeyStoreKey()?.IsSet() ?? false);

            if (null == groupWithKey)
            {
                throw new OdinSecurityException($"No key store key found");
            }

            return groupWithKey.GetKeyStoreKey();
        }
        public SensitiveByteArray GetIcrKey()
        {
            foreach (var group in PermissionGroups.Values)
            {
                var key = group.GetIcrKey();
                if (key?.IsSet() ?? false)
                {
                    //TODO: log key as source of permission.
                    return key;
                }
            }

            throw new OdinSecurityException($"No access permitted to the Icr Key");
        }

        public bool HasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (_isSystem)
            {
                return true;
            }

            foreach (var key in PermissionGroups.Keys)
            {
                var group = PermissionGroups[key];
                if (group.HasDrivePermission(driveId, permission))
                {
                    //TODO: log key as source of permission.
                    return true;
                }
            }

            return false;
        }

        public void AssertHasAtLeastOneDrivePermission(Guid driveId, params DrivePermission[] permissions)
        {
            if (!permissions.Any(p => HasDrivePermission(driveId, p)))
            {
                throw new OdinSecurityException($"Unauthorized access to drive [{driveId}]");
            }
        }

        public void AssertHasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (!this.HasDrivePermission(driveId, permission))
            {
                throw new OdinSecurityException($"Unauthorized access to {permission} to drive [{driveId}]");
            }
        }

        public void AssertHasDrivePermission(TargetDrive targetDrive, DrivePermission permission)
        {
            var driveId = this.GetDriveId(targetDrive);
            if (!this.HasDrivePermission(driveId, permission))
            {
                throw new OdinSecurityException($"Unauthorized access to {permission} to drive [{driveId}]");
            }
        }

        public bool HasPermission(int permissionKey)
        {
            if (_isSystem)
            {
                return true;
            }

            foreach (var key in PermissionGroups.Keys)
            {
                var group = PermissionGroups[key];
                if (group.HasPermission(permissionKey))
                {
                    //TODO: log key as source of permission.
                    return true;
                }
            }

            return false;
        }

        public void AssertHasAtLeastOnePermission(params int[] permissionKeys)
        {
            if (!HasAtLeastOnePermission(permissionKeys))
            {
                throw new OdinSecurityException("Does not have permission");
            }
        }

        public bool HasAtLeastOnePermission(params int[] permissionKeys)
        {
            return permissionKeys.Any(HasPermission);
        }

        public void AssertHasPermission(int permissionKey)
        {
            if (!HasPermission(permissionKey))
            {
                throw new OdinSecurityException("Does not have permission");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanWriteToDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermission.Write))
            {
                throw new OdinSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanReadDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermission.Read))
            {
                throw new OdinSecurityException($"Unauthorized to read to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public bool HasDriveId(TargetDrive drive, out Guid? driveId)
        {
            if (null == drive)
            {
                throw new OdinClientException("target drive not specified", OdinClientErrorCode.InvalidTargetDrive);
            }

            driveId = GetDriveIdInternal(drive);
            return driveId.HasValue;
        }

        public Guid GetDriveId(TargetDrive drive)
        {
            OdinValidationUtils.AssertIsValidTargetDriveValue(drive);

            var driveId = GetDriveIdInternal(drive);

            if (driveId.HasValue)
            {
                return driveId.Value;
            }

            throw new OdinSecurityException($"No access permitted to drive alias {drive.Alias} and drive type {drive.Type}");
        }

        private Guid? GetDriveIdInternal(TargetDrive drive)
        {
            foreach (var key in PermissionGroups.Keys)
            {
                var group = PermissionGroups[key];
                var driveId = group.GetDriveId(drive);
                if (driveId.HasValue)
                {
                    return driveId.Value;
                }
            }

            return null;
        }

        public TargetDrive GetTargetDrive(Guid driveId)
        {
            foreach (var key in PermissionGroups.Keys)
            {
                var group = PermissionGroups[key];
                var td = group.GetTargetDrive(driveId);
                if (null != td)
                {
                    //TODO: log key as source of permission.
                    return td;
                }
            }

            throw new OdinSecurityException($"No access permitted to drive {driveId}");
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            if (TryGetDriveStorageKey(driveId, out var storageKey))
            {
                return storageKey;
            }

            //TODO: this sort of security check feels like it should be in a service..
            throw new OdinSecurityException($"No access permitted to drive {driveId}");
        }

        public bool TryGetDriveStorageKey(Guid driveId, out SensitiveByteArray storageKey)
        {
            storageKey = null;
            foreach (var key in PermissionGroups.Keys)
            {
                var group = PermissionGroups[key];
                storageKey = group.GetDriveStorageKey(driveId, out var grantCount);

                if (grantCount > 1)
                {
                    var td = GetTargetDrive(driveId);
                    Log.Warning("Permission group with Key [{key}] has {grantCount} grants for drive [{td}]", key, grantCount, td);
                }

                var value = storageKey?.GetKey() ?? Array.Empty<byte>();

                if (value.Length > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public RedactedPermissionContext Redacted()
        {
            return new RedactedPermissionContext()
            {
                PermissionGroups = PermissionGroups.Values.Select(pg => pg.Redacted()),
            };
        }
    }

    public class RedactedPermissionContext
    {
        public IEnumerable<RedactedPermissionGroup> PermissionGroups { get; init; }
    }
}