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

        public bool HasDrivePermission(Guid driveId, DrivePermissions permission)
        {
            if (this._isOwner)
            {
                return true;
            }

            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permissions.HasFlag(permission);
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
                        return ((ContactPermissions) value).HasFlag((ContactPermissions) permission);

                    case SystemApi.CircleNetwork:
                        return ((CircleNetworkPermissions) value).HasFlag((CircleNetworkPermissions) permission);

                    case SystemApi.CircleNetworkRequests:
                        return ((CircleNetworkRequestPermissions) value).HasFlag((CircleNetworkRequestPermissions) permission);
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
            if (!this.HasDrivePermission(driveId, DrivePermissions.Write))
            {
                throw new YouverseSecurityException($"Unauthorized to write to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Determines if the current request can write to the specified drive
        /// </summary>
        public void AssertCanReadDrive(Guid driveId)
        {
            if (!this.HasDrivePermission(driveId, DrivePermissions.Read))
            {
                throw new YouverseSecurityException($"Unauthorized to read to drive [{driveId}]");
            }
        }

        /// <summary>
        /// Returns the encryption key specific to this app.  This is only available
        /// when the owner is making an HttpRequest.
        /// </summary>
        /// <returns></returns>
        public Guid GetDriveId(Guid driveAlias)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveAlias == driveAlias);

            //TODO: this sort of security check feels like it should be in a service..
            if (null == grant)
            {
                throw new YouverseSecurityException($"No access permitted to drive alias {driveAlias}");
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
                throw new YouverseSecurityException($"No access permitted to drive {driveId}");
            }

            var key = this._driveDecryptionKey;
            var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(ref key);
            return storageKey;
        }
    }
}