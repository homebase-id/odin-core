using Odin.Core.Exceptions;
using Odin.Core.Identity;

namespace Odin.Core.Services.Base
{
    public class OdinContext
    {
        private PermissionContext _permissionsContext;
        private string _authContext;

        public string AuthContext
        {
            get
            {
                return _authContext;
            }
        }

        public OdinId Tenant { get; set; }
        
        public CallerContext Caller { get; set; }

        public OdinId GetCallerOdinIdOrFail()
        {
            return Caller.OdinId ?? throw new YouverseSystemException("Invalid Caller");
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

        public void SetAuthContext(string authContext)
        {
            //This is only exist to ensure we only set auth context in the DotYouContextMiddleware
            if (null != _authContext)
            {
                throw new YouverseSecurityException("Cannot reset auth context");
            }

            _authContext = authContext;
        }
        
        public void AssertCanManageConnections()
        {
            if (this.Caller.IsOwner && this.Caller.HasMasterKey)
            {
                return;
            }

            throw new YouverseSecurityException("Unauthorized Action");
        }


        public RedactedOdinContext Redacted()
        {
            return new RedactedOdinContext()
            {
                Caller = this.Caller.Redacted(),
                PermissionContext = this.PermissionsContext.Redacted()
            };
        }
        
    }
    
    public class RedactedOdinContext
    {
        public RedactedCallerContext Caller { get; set; }
        public RedactedPermissionContext PermissionContext { get; set; }
    }
}