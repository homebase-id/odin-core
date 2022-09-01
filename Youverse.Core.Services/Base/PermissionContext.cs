using System;
using System.Collections.Generic;
using System.Linq;
using System.Xaml.Permissions;
using Dawn;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Specifies a set of permissions.  This allows an identity's permissions to come from multiple sources such as circles.
    /// </summary>
    public class PermissionGroup
    {
        private readonly PermissionSet _permissionSet;
        private readonly IEnumerable<DriveGrant> _driveGrants;
        private readonly SensitiveByteArray _driveDecryptionKey;

        public PermissionGroup(PermissionSet permissionSet, IEnumerable<DriveGrant> driveGrants, SensitiveByteArray driveDecryptionKey)
        {
            _permissionSet = permissionSet;
            _driveGrants = driveGrants;
            _driveDecryptionKey = driveDecryptionKey;
        }

        public bool HasDrivePermission(Guid driveId, DrivePermission permission)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permission.HasFlag(permission);
        }

        public bool HasPermission(PermissionFlags permission)
        {
            return this._permissionSet.Permissions.HasFlag(permission);
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public Guid? GetDriveId(TargetDrive drive)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.Drive.Alias == drive.Alias && g.Drive.Type == drive.Type);
            return grant?.DriveId;
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);

            if (null == grant)
            {
                return null;
            }

            //If we cannot decrypt the storage key BUT the caller has access to the drive,
            //this most likely denotes an anonymous drive.  Return an empty key which means encryption will fail
            if (this._driveDecryptionKey == null || grant.KeyStoreKeyEncryptedStorageKey == null)
            {
                return Array.Empty<byte>().ToSensitiveByteArray();
            }

            var key = this._driveDecryptionKey;
            var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(ref key);
            return storageKey;
        }
    }

    public class PermissionContext
    {
        private readonly Dictionary<string, PermissionGroup> _permissionGroups;
        private readonly bool _isOwner = false;

        public PermissionContext(
            Dictionary<string, PermissionGroup> permissionGroups,
            SensitiveByteArray sharedSecretKey,
            bool isOwner)
        {
            Guard.Argument(permissionGroups, nameof(permissionGroups)).NotNull();

            this.SharedSecretKey = sharedSecretKey;
            _permissionGroups = permissionGroups;

            //HACK: need to actually assign the permission
            this._isOwner = isOwner;
        }

        public SensitiveByteArray SharedSecretKey { get; }

        public bool HasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (this._isOwner)
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

        public bool HasPermission(PermissionFlags permission)
        {
            if (this._isOwner)
            {
                return true;
            }

            foreach (var key in _permissionGroups.Keys)
            {
                var group = _permissionGroups[key];
                if (group.HasPermission(permission))
                {
                    //TODO: log key as source of permission.
                    return true;
                }
            }

            return false;
        }

        public void AssertHasPermission(PermissionFlags permission)
        {
            if (!HasPermission(permission))
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
                throw new DriveSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanReadDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermission.Read))
            {
                throw new DriveSecurityException($"Unauthorized to read to drive [{driveId}]");
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

            throw new DriveSecurityException($"No access permitted to drive alias {drive.Alias} and drive type {drive.Type}");
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
            throw new DriveSecurityException($"No access permitted to drive {driveId}");
        }
    }
}