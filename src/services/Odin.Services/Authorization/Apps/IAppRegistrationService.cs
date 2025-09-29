#nullable enable

using System;
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
        Task<RedactedAppRegistration> RegisterAppAsync(AppRegistrationRequest request, IOdinContext odinContext);

        Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext);

        Task<IOdinContext?> GetAppPermissionContextAsync(ClientAuthenticationToken token, IOdinContext odinContext);

        /// <summary>
        /// Updates the permissions granted to the app
        /// </summary>
        Task UpdateAppPermissionsAsync(UpdateAppPermissionsRequest request, IOdinContext odinContext);

        /// <summary>
        /// Updates the authorized circles and the permissions granted to them
        /// </summary>
        /// <returns></returns>
        Task UpdateAuthorizedCirclesAsync(UpdateAuthorizedCirclesRequest request, IOdinContext odinContext);

        Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthTokenAsync(
            ClientAuthenticationToken authToken,
            IOdinContext odinContext);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetRegisteredAppsAsync(IOdinContext odinContext);

        /// <summary>
        /// Gets all apps which grant the specified circle
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetAppsGrantingCircleAsync(Guid circleId, IOdinContext odinContext);

        /// <summary>
        /// Removes access for a given application across all devices
        /// </summary>
        Task RevokeAppAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        Task RemoveAppRevocationAsync(GuidId appId, IOdinContext odinContext);

        // Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPkAsync(GuidId appId,
        //     byte[] clientPublicKey,
        //     string friendlyName, IOdinContext odinContext);


        /// <summary>
        /// Registers an application to be used on a given device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="friendlyName"></param>
        /// <param name="odinContext"></param>
        /// <returns></returns>
        Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(GuidId appId, string friendlyName, IOdinContext odinContext);

        Task<List<RegisteredAppClientResponse>> GetRegisteredClientsAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// Revokes a client from using the app
        /// </summary>
        Task RevokeClientAsync(GuidId accessRegistrationId, IOdinContext odinContext);

        Task DeleteClientAsync(GuidId accessRegistrationId, IOdinContext odinContext);

        Task AllowClientAsync(GuidId accessRegistrationId, IOdinContext odinContext);

        Task DeleteAppAsync(GuidId appId, IOdinContext odinContext);

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        Task DeleteCurrentAppClientAsync(IOdinContext odinContext);
    }
}