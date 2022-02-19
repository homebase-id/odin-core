using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base
{
    public class PermissionContext
    {
        private Dictionary<PermissionType, int> _permissionGrants;
        private List<DriveGrant> _driveGrants;

        public PermissionContext(List<DriveGrant> driveGrants, Dictionary<PermissionType, int> permissionGrants)
        {
            _driveGrants = driveGrants;
            _permissionGrants = permissionGrants;
        }

        public bool HasDrivePermission(Guid driveId, DrivePermissions permission)
        {
            var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
            return grant != null && grant.Permissions.HasFlag(permission);
        }

        public bool HasPermission(PermissionType pmt, int permission)
        {
            if (_permissionGrants.TryGetValue(pmt, out var value))
            {
                switch (pmt)
                {
                    case PermissionType.Contact:
                        return ((ContactPermissions) value).HasFlag((ContactPermissions) permission);
                    
                    case PermissionType.CircleNetwork:
                        return ((CircleNetworkPermissions) value).HasFlag((CircleNetworkPermissions) permission);
                    
                    case PermissionType.CircleNetworkRequests:
                        return ((CircleNetworkRequestPermissions) value).HasFlag((CircleNetworkRequestPermissions) permission);
                }
            }

            return false;
        }

        public void AssertHasPermission(PermissionType pmt, int permission)
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

    }
}