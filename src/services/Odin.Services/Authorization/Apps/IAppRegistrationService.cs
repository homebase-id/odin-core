#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request, OdinContext odinContext);

        Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, OdinContext odinContext);

        Task<OdinContext?> GetAppPermissionContext(ClientAuthenticationToken token, OdinContext odinContext);

        /// <summary>
        /// Updates the permissions granted to the app
        /// </summary>
        Task UpdateAppPermissions(UpdateAppPermissionsRequest request, OdinContext odinContext);

        /// <summary>
        /// Updates the authorized circles and the permissions granted to them
        /// </summary>
        /// <returns></returns>
        Task UpdateAuthorizedCircles(UpdateAuthorizedCirclesRequest request, OdinContext odinContext);

        Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(ClientAuthenticationToken authToken,
            OdinContext odinContext);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetRegisteredApps(OdinContext odinContext);

        /// <summary>
        /// Removes access for a given application across all devices
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        Task RevokeApp(GuidId appId, OdinContext odinContext);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        Task RemoveAppRevocation(GuidId appId, OdinContext odinContext);

        Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPk(GuidId appId, byte[] clientPublicKey,
            string friendlyName, OdinContext odinContext);


        /// <summary>
        /// Registers an application to be used on a given device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="friendlyName"></param>
        /// <returns></returns>
        Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(GuidId appId, string friendlyName, OdinContext odinContext);

        Task<List<RegisteredAppClientResponse>> GetRegisteredClients(GuidId appId, OdinContext odinContext);

        /// <summary>
        /// Revokes a client from using the app
        /// </summary>
        Task RevokeClient(GuidId accessRegistrationId, OdinContext odinContext);

        Task DeleteClient(GuidId accessRegistrationId, OdinContext odinContext);

        Task AllowClient(GuidId accessRegistrationId, OdinContext odinContext);

        Task DeleteApp(GuidId appId, OdinContext odinContext);

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        Task DeleteCurrentAppClient(OdinContext odinContext);
    }
}