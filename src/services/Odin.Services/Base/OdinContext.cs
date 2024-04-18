using System;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Services.Base
{
    public interface IOdinContext
    {
        string AuthContext { get; }
        OdinId Tenant { get; set; }
        CallerContext Caller { get; set; }
        PermissionContext PermissionsContext { get; }

        /// <summary>
        /// The age of the <see cref="ClientAuthenticationToken"/>
        /// </summary>
        UnixTimeUtc? AuthTokenCreated { get; set; }

        OdinId GetCallerOdinIdOrFail();
        void SetPermissionContext(PermissionContext pc);
        void SetAuthContext(string authContext);
        void AssertCanManageConnections();
        RedactedOdinContext Redacted();
    }

    public class OdinContext : IOdinContext
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
            return Caller.OdinId ?? throw new OdinSystemException("Invalid Caller");
        }

        public PermissionContext PermissionsContext
        {
            get { return _permissionsContext; }
        }


        /// <summary>
        /// The age of the <see cref="ClientAuthenticationToken"/>
        /// </summary>
        public UnixTimeUtc? AuthTokenCreated { get; set; }

        public void SetPermissionContext(PermissionContext pc)
        {
            //This is only exist to ensure we only set permissions in the DotYouContextMiddleware
            if (null != _permissionsContext)
            {
                throw new OdinSecurityException("Cannot set permission context");
            }

            _permissionsContext = pc;
        }

        public void SetAuthContext(string authContext)
        {
            //This is only exist to ensure we only set auth context in the DotYouContextMiddleware
            if (null != _authContext)
            {
                throw new OdinSecurityException("Cannot reset auth context");
            }

            _authContext = authContext;
        }
        
        public void AssertCanManageConnections()
        {
            if (this.Caller.IsOwner && this.Caller.HasMasterKey)
            {
                return;
            }

            throw new OdinSecurityException("Unauthorized Action");
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