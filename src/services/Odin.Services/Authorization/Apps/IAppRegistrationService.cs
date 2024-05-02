#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request, IOdinContext odinContext, DatabaseConnection cn);

        Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId, IOdinContext odinContext, DatabaseConnection cn);

        Task<IOdinContext?> GetAppPermissionContext(ClientAuthenticationToken token, IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Updates the permissions granted to the app
        /// </summary>
        Task UpdateAppPermissions(UpdateAppPermissionsRequest request, IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Updates the authorized circles and the permissions granted to them
        /// </summary>
        /// <returns></returns>
        Task UpdateAuthorizedCircles(UpdateAuthorizedCirclesRequest request, IOdinContext odinContext, DatabaseConnection cn);

        Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(ClientAuthenticationToken authToken,
            IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<List<RedactedAppRegistration>> GetRegisteredApps(IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Removes access for a given application across all devices
        /// </summary>
        Task RevokeApp(GuidId appId, IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        Task RemoveAppRevocation(GuidId appId, IOdinContext odinContext, DatabaseConnection cn);

        Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPk(GuidId appId, byte[] clientPublicKey,
            string friendlyName, IOdinContext odinContext, DatabaseConnection cn);


        /// <summary>
        /// Registers an application to be used on a given device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="friendlyName"></param>
        /// <param name="odinContext"></param>
        /// <param name="cn"></param>
        /// <returns></returns>
        Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(GuidId appId, string friendlyName, IOdinContext odinContext, DatabaseConnection cn);

        Task<List<RegisteredAppClientResponse>> GetRegisteredClients(GuidId appId, IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Revokes a client from using the app
        /// </summary>
        Task RevokeClient(GuidId accessRegistrationId, IOdinContext odinContext, DatabaseConnection cn);

        Task DeleteClient(GuidId accessRegistrationId, IOdinContext odinContext, DatabaseConnection cn);

        Task AllowClient(GuidId accessRegistrationId, IOdinContext odinContext, DatabaseConnection cn);

        Task DeleteApp(GuidId appId, IOdinContext odinContext, DatabaseConnection cn);

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        Task DeleteCurrentAppClient(IOdinContext odinContext, DatabaseConnection cn);
    }
}