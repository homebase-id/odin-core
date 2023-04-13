﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;

#nullable enable

namespace Youverse.Core.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request);

        Task<RedactedAppRegistration> GetAppRegistration(GuidId appId);

        Task<DotYouContext> GetAppPermissionContext(ClientAuthenticationToken token);

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

        /// <summary>
        /// Registers an application on a given device.  Returns the information required by the device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="clientPublicKey">The RSA public key generated by the client; to be used to encrypt the response</param>
        /// <param name="friendlyName"></param>
        /// >
        /// <returns></returns>
        Task<AppClientRegistrationResponse> RegisterClient(GuidId appId, byte[] clientPublicKey, string friendlyName);

        Task<List<RegisteredAppClientResponse>> GetRegisteredClients();

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