using Youverse.Core.Exceptions;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Base
{
    public class DotYouContext
    {
        private PermissionContext _permissionsContext;
        
        public string AuthContext { get; set; }

        public CallerContext Caller { get; set; }

        public OdinId GetCallerOdinIdOrFail()
        {
            return Caller.DotYouId ?? throw new YouverseSystemException("Invalid Caller");
        }

        public PermissionContext PermissionsContext
        {
            get { return _permissionsContext; }
        }
        
        public void SetPermissionContext(PermissionContext pc)
        {
            //This is only exist to ensure we only set permissions in the DotYouContextMiddleware
            if (null != _permissionsContext)
            {
                throw new YouverseSecurityException("Cannot set permission context");
            }

            _permissionsContext = pc;
        }

        public void AssertCanManageConnections()
        {
            if (this.Caller.IsOwner && this.Caller.HasMasterKey)
            {
                return;
            }

            throw new YouverseSecurityException("Unauthorized Action");
        }
    }
}