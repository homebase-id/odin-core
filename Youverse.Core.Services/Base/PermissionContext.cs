using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class PermissionContext
    {
        private readonly IEnumerable<DriveGrant> _driveGrants;
        private readonly PermissionSet _permissionSet;
        private readonly SensitiveByteArray _driveDecryptionKey;
        private readonly bool _isOwner = false;

        public PermissionContext(
            IEnumerable<DriveGrant> driveGrants,
            PermissionSet permissionSet,
            SensitiveByteArray driveDecryptionKey,
            SensitiveByteArray sharedSecretKey,
            Guid exchangeGrantId,
            Guid accessRegistrationId,
            bool isOwner)
        {
            this._driveGrants = driveGrants;
            this._permissionSet = permissionSet;
            this._driveDecryptionKey = driveDecryptionKey;
            this.SharedSecretKey = sharedSecretKey;
            this.ExchangeGrantId = exchangeGrantId;
            this.AccessRegistrationId = accessRegistrationId;

            //HACK: need to actually assign the permission
            this._isOwner = isOwner;
        }

        public Guid ExchangeGrantId { get; }

        public SensitiveByteArray SharedSecretKey { get; }

        public Guid AccessRegistrationId { get; }

        public bool HasDrivePermission(Guid driveId, DrivePermission permission)
        {
            if (this._isOwner)
            {
                return true;
            }

            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permission.HasFlag(permission);
        }

        public bool HasPermission(SystemApi pmt, int permission)
        {
            if (this._isOwner)
            {
                return true;
            }

            if (null == _permissionSet || _permissionSet.Permissions?.Count == 0)
            {
                return false;
            }

            if (_permissionSet.Permissions!.TryGetValue(pmt, out var value))
            {
                switch (pmt)
                {
                    case SystemApi.Contact:
                        return ((ContactPermissions)value).HasFlag((ContactPermissions)permission);

                    case SystemApi.CircleNetwork:
                        return ((CircleNetworkPermissions)value).HasFlag((CircleNetworkPermissions)permission);

                    case SystemApi.CircleNetworkRequests:
                        return ((CircleNetworkRequestPermissions)value).HasFlag((CircleNetworkRequestPermissions)permission);
                }
            }

            return false;
        }

        public void AssertHasPermission(SystemApi pmt, int permission)
        {
            if (!HasPermission(pmt, permission))
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
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveAlias == drive.Alias && g.DriveType == drive.Type);

            //TODO: this sort of security check feels like it should be in a service..
            if (null == grant)
            {
                throw new DriveSecurityException($"No access permitted to drive alias {drive.Alias} and drive type {drive.Type}");
            }

            return grant.DriveId;
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);

            //TODO: this sort of security check feels like it should be in a service..
            if (null == grant)
            {
                throw new DriveSecurityException($"No access permitted to drive {driveId}");
            }

            //If we cannot decrypt the storage key BUT the caller has access to the drive,
            //this most likely denotes an anonymous drive.  Return an empty key which means encryption will fail
            if (this._driveDecryptionKey == null || grant.KeyStoreKeyEncryptedStorageKey == null)
            {
                throw new DriveSecurityException($"Caller has access {driveId} but exchange grant does not have a drive decryption key or KeyStoreKeyEncryptedStorageKey");
                // return Array.Empty<byte>().ToSensitiveByteArray();
            }

            var key = this._driveDecryptionKey;
            var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(ref key);
            return storageKey;
        }
    }
}