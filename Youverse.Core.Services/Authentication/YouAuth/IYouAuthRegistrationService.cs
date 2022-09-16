using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    /// <summary>
    /// Manages the registered <see cref="DotYouIdentity"/>'s  who are 'logged in' to this Identity
    /// </summary>
    public interface IYouAuthRegistrationService
    {
        /// <summary>
        /// Grants access to the <see cref="dotYouId"/>
        /// </summary>
        ValueTask<ClientAccessToken> RegisterYouAuthAccess(string dotYouId, ClientAuthenticationToken remoteIcrClientAuthToken);

        // ValueTask<YouAuthRegistration?> LoadFromId(Guid id);
        ValueTask<YouAuthRegistration?> LoadFromSubject(string subject);
        ValueTask DeleteFromSubject(string subject);

        ValueTask<(DotYouIdentity dotYouId, bool isValid, bool isConnected, PermissionContext permissionContext, List<ByteArrayId> enabledCircleIds)> GetPermissionContext(
            ClientAuthenticationToken authToken);
    }
}