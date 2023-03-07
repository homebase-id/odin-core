using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Youverse.Core.Identity;
using Youverse.Core.Services.AppNotifications.ClientNotifications;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;

#nullable enable

namespace Youverse.Core.Services.Authentication.YouAuth
{
    /// <summary>
    /// Manages the registered <see cref="OdinId"/>'s  who are 'logged in' to this Identity
    /// </summary>
    public interface IYouAuthRegistrationService : INotificationHandler<IdentityConnectionRegistrationChangedNotification>
    {
        /// <summary>
        /// Grants access to the <see cref="odinId"/>
        /// </summary>
        ValueTask<ClientAccessToken> RegisterYouAuthAccess(string odinId, ClientAuthenticationToken remoteIcrClientAuthToken);

        // ValueTask<YouAuthRegistration?> LoadFromId(Guid id);
        ValueTask<YouAuthRegistration?> LoadFromSubject(string subject);
        ValueTask DeleteFromSubject(string subject);

        Task<DotYouContext?> GetDotYouContext(ClientAuthenticationToken token);

        ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken);
    }
}