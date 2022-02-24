using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains all information required to execute commands in the Youverse.Core.Services services.
    /// </summary>
    public class DotYouContextAccessor
    {
        private readonly IHttpContextAccessor _accessor;

        public DotYouContextAccessor(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public DotYouContext GetCurrent()
        {
            return _accessor.HttpContext.RequestServices.GetRequiredService<DotYouContext>();
        }
    }

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