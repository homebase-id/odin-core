using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Cryptography;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class PermissionContext
    {
        private readonly Dictionary<SystemApiPermissionType, int> _systemApiPermissionGrants;
        private readonly IEnumerable<PermissionDriveGrant> _driveGrants;
        private readonly SensitiveByteArray _driveDecryptionKey;

        public PermissionContext(IEnumerable<PermissionDriveGrant> driveGrants, Dictionary<SystemApiPermissionType, int> systemApiPermissionGrants, SensitiveByteArray driveDecryptionKey)
        {
            _driveGrants = driveGrants;
            _systemApiPermissionGrants = systemApiPermissionGrants;
            _driveDecryptionKey = driveDecryptionKey;
        }

        public bool HasDrivePermission(Guid driveId, DrivePermissions permission)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permissions.HasFlag(permission);
        }

        public bool HasPermission(SystemApiPermissionType pmt, int permission)
        {
            if (null == _systemApiPermissionGrants)
            {
                return false;
            }
            
            if (_systemApiPermissionGrants.TryGetValue(pmt, out var value))
            {
                switch (pmt)
                {
                    case SystemApiPermissionType.Contact:
                        return ((ContactPermissions) value).HasFlag((ContactPermissions) permission);

                    case SystemApiPermissionType.CircleNetwork:
                        return ((CircleNetworkPermissions) value).HasFlag((CircleNetworkPermissions) permission);

                    case SystemApiPermissionType.CircleNetworkRequests:
                        return ((CircleNetworkRequestPermissions) value).HasFlag((CircleNetworkRequestPermissions) permission);
                }
            }

            return false;
        }

        public void AssertHasPermission(SystemApiPermissionType pmt, int permission)
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
        public SensitiveByteArray GetDriveStorageKey(Guid driveId)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);

            //TODO: this sort of security check feels like it should be in a service..
            if (null == grant)
            {
                throw new YouverseSecurityException($"No access permitted to drive {driveId}");
            }

            var appKey = this._driveDecryptionKey;
            var storageKey = grant.EncryptedStorageKey.DecryptKeyClone(ref appKey);
            return storageKey;
        }
    }
}