using System;
using System.Collections.Generic;
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
            return false;
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
    }
}