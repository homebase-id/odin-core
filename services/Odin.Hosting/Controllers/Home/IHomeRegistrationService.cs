#nullable enable

using System.Threading.Tasks;
using MediatR;
using Odin.Core.Identity;
using Odin.Core.Services.AppNotifications.ClientNotifications;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;

namespace Odin.Hosting.Controllers.Home
{
    /// <summary>
    /// Manages the registered <see cref="OdinId"/>'s  who are 'logged in' to this Identity
    /// </summary>
    public interface IHomeRegistrationService : INotificationHandler<IdentityConnectionRegistrationChangedNotification>
    {
        /// <summary>
        /// Grants access to the <see cref="odinId"/>
        /// </summary>
        ValueTask<ClientAccessToken> RegisterYouAuthAccess(string odinId, ClientAuthenticationToken clientAuthToken);

        // ValueTask<YouAuthRegistration?> LoadFromId(Guid id);
        ValueTask<YouAuthRegistration?> LoadFromSubject(string subject);
        ValueTask DeleteFromSubject(string subject);

        Task<OdinContext?> GetDotYouContext(ClientAuthenticationToken token);

        ValueTask<(CallerContext? callerContext, PermissionContext? permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken);
    }
}