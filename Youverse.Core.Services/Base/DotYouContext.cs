using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Base
{
    public class DotYouContext
    {
        private PermissionContext _permissions;

        public CallerContext Caller { get; set; }

        public IAppContext AppContext { get; set; }

        public PermissionContext Permissions
        {
            get { return _permissions; }
        }

        public void SetPermissionContext(PermissionContext pc)
        {
            //This is only exist to ensure we only set permissions in the DotYouContextMiddleware
            if (null != _permissions)
            {
                throw new YouverseSecurityException("Cannot set permission context");
            }

            _permissions = pc;
        }

        public void AssertCanManageConnections()
        {
            if (this.Caller.IsOwner && this.Caller.HasMasterKey)
            {
                return;
            }

            if (Permissions != null && Permissions.HasPermission(SystemApiPermissionType.CircleNetwork, (int) CircleNetworkPermissions.Manage))
            {
                return;
            }

            throw new YouverseSecurityException("Unauthorized Action");
        }
    }
}