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
        Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request);

        Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId);

        Task<OdinContext?> GetAppPermissionContext(ClientAuthenticationToken token);

        /// <summary>
        /// Updates the permissions granted to the app
        /// </summary>
        Task UpdateAppPermissions(UpdateAppPermissionsRequest request);

        /// <summary>
        /// Updates the authorized circles and the permissions granted to them
        /// </summary>
        /// <returns></returns>
        Task UpdateAuthorizedCircles(UpdateAuthorizedCirclesRequest request);

        Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(ClientAuthenticationToken authToken);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetRegisteredApps();

        /// <summary>
        /// Removes access for a given application across all devices
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        Task RevokeApp(GuidId appId);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        Task RemoveAppRevocation(GuidId appId);

        Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPk(GuidId appId, byte[] clientPublicKey, string friendlyName);

        
        /// <summary>
        /// Registers an application to be used on a given device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="friendlyName"></param>
        /// <returns></returns>
        Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(GuidId appId, string friendlyName);
        
        Task<List<RegisteredAppClientResponse>> GetRegisteredClients(GuidId appId);

        /// <summary>
        /// Revokes a client from using the app
        /// </summary>
        Task RevokeClient(GuidId accessRegistrationId);

        Task DeleteClient(GuidId accessRegistrationId);

        Task AllowClient(GuidId accessRegistrationId);

        Task DeleteApp(GuidId appId);

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        Task DeleteCurrentAppClient();
    }
}