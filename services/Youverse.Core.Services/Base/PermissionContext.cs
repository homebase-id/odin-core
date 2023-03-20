using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Base
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
            Guard.Argument(permissionGroups, nameof(permissionGroups)).NotNull();

            this.SharedSecretKey = sharedSecretKey;
            _permissionGroups = permissionGroups;

            // _instanceId = new Guid();
            _isSystem = isSystem;
        }

        public SensitiveByteArray SharedSecretKey { get; }

        public bool HasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (_isSystem)
            {
                return true;
            }

            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                if (group.HasDrivePermission(driveId, permission))
                {
                    //TODO: log key as source of permission.
                    return true;
                }
            }

            return false;
        }

        public void AssertHasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (!this.HasDrivePermission(driveId, permission))
            {
                throw new YouverseSecurityException($"Unauthorized access to {permission} to drive [{driveId}]");
            }
        }

        public bool HasPermission(int permissionKey)
        {
            if (_isSystem)
            {
                return true;
            }

            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                if (group.HasPermission(permissionKey))
                {
                    //TODO: log key as source of permission.
                    return true;
                }
            }

            return false;
        }

        public void AssertHasPermission(int permissionKey)
        {
            if (!HasPermission(permissionKey))
            {
                throw new YouverseSecurityException("Does not have permission");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanWriteToDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermission.Write))
            {
                throw new YouverseSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }

        public void AssertCanWriteReactionsAndCommentsToDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermission.WriteReactionsAndComments))
            {
                throw new YouverseSecurityException($"Unauthorized to write reactions and comments to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanReadDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermission.Read))
            {
                throw new YouverseSecurityException($"Unauthorized to read to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public Guid GetDriveId(TargetDrive drive)
        {
            if (null == drive)
            {
                throw new YouverseClientException("target drive not specified", YouverseClientErrorCode.InvalidTargetDrive);
            }

            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                var driveId = group.GetDriveId(drive);
                if (driveId.HasValue)
                {
                    //TODO: log key as source of permission.
                    return driveId.Value;
                }
            }

            throw new YouverseSecurityException($"No access permitted to drive alias {drive.Alias} and drive type {drive.Type}");
        }

        public TargetDrive GetTargetDrive(Guid driveId)
        {
            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                var td = group.GetTargetDrive(driveId);
                if (null != td)
                {
                    //TODO: log key as source of permission.
                    return td;
                }
            }

            throw new YouverseSecurityException($"No access permitted to drive {driveId}");
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                var storageKey = group.GetDriveStorageKey(driveId);
                if (storageKey != null)
                {
                    //TODO: log key as source of permission.
                    return storageKey;
                }
            }

            //TODO: this sort of security check feels like it should be in a service..
            throw new YouverseSecurityException($"No access permitted to drive {driveId}");
        }

        public bool TryGetDriveStorageKey(Guid driveId, out SensitiveByteArray storageKey)
        {
            storageKey = null;
            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                storageKey = group.GetDriveStorageKey(driveId);
                if (storageKey != null)
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
                PermissionGroups = _permissionGroups.Values.Select(pg => pg.Redacted()),
            };
        }
    }

    public class RedactedPermissionContext
    {
        public IEnumerable<RedactedPermissionGroup> PermissionGroups { get; init; }
    }
}