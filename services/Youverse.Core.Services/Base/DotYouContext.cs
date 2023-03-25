using System;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Base
{
    public class DotYouContext
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


        public RedactedDotYouContext Redacted()
        {
            return new RedactedDotYouContext()
            {
                Caller = this.Caller.Redacted(),
                PermissionContext = this.PermissionsContext.Redacted()
            };
        }
        
    }
    
    public class RedactedDotYouContext
    {
        public RedactedCallerContext Caller { get; set; }
        public RedactedPermissionContext PermissionContext { get; set; }
    }
}