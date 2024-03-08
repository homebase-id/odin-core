using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Drives;
using Odin.Services.Util;
using Serilog;

namespace Odin.Services.Base
{
    public class PermissionContext
    {
        private readonly Dictionary<string, PermissionGroup> _permissionGroups;
        private readonly bool _isSystem = false;

        // private Guid _instanceId;

        public PermissionContext(
            Dictionary<string, PermissionGroup> permissionGroups,
            SensitiveByteArray sharedSecretKey,
            bool isSystem = false)
        {
            this.SharedSecretKey = sharedSecretKey;
            // IcrKey = icrKey;
            
            _permissionGroups = permissionGroups;

            // _instanceId = new Guid();
            _isSystem = isSystem;
        }

        public SensitiveByteArray SharedSecretKey { get; }

        internal Dictionary<string, PermissionGroup> PermissionGroups => _permissionGroups;

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

        private bool HasPermission(int permissionKey)
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
            if (!permissionKeys.Any(HasPermission))
            {
                throw new OdinSecurityException("Does not have permission");
            }
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