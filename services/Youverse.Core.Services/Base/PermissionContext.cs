using System;
using System.Collections.Generic;
using System.Linq;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class PermissionContext
    {
        private readonly Dictionary<string, PermissionGroup> _permissionGroups;
        private readonly bool _isOwner = false;
        private readonly bool _isSystem = false;

        public PermissionContext(
            Dictionary<string, PermissionGroup> permissionGroups,
            SensitiveByteArray sharedSecretKey,
            bool isOwner,
            bool isSystem = false)
        {
            Guard.Argument(permissionGroups, nameof(permissionGroups)).NotNull();

            this.SharedSecretKey = sharedSecretKey;
            _permissionGroups = permissionGroups;

            this._isOwner = isOwner;
            _isSystem = isSystem;
        }

        public SensitiveByteArray SharedSecretKey { get; }

        public bool HasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (this._isOwner || _isSystem)
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

        public bool HasPermission(int permissionKey)
        {
            if (this._isOwner || _isSystem)
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

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            // var hack = _permissionGroups.Keys.Where(k => k != "anonymous_drives");
            //
            // //TODO: Update the search pattern to prioritize drives with keys over the anonmyous drive grants
            // foreach (var key in hack)
            // {
            //     var group = _permissionGroups[key];
            //     var storageKey = group.GetDriveStorageKey(driveId);
            //     if (storageKey != null)
            //     {
            //         //TODO: log key as source of permission.
            //         return storageKey;
            //     }
            // }

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
    }
}