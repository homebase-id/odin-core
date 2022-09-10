﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        Task<RedactedAppRegistration> RegisterApp(ByteArrayId appId, string name, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives);

        Task<RedactedAppRegistration> GetAppRegistration(ByteArrayId appId);

        Task<(ByteArrayId appId, PermissionContext permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken);

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
        Task RevokeApp(ByteArrayId appId);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        Task RemoveAppRevocation(ByteArrayId appId);

        /// <summary>
        /// Registers an application on a given device.  Returns the information required by the device
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="clientPublicKey">The RSA public key generated by the client; to be used to encrypt the response</param>
        /// <param name="friendlyName"></param>
        /// >
        /// <returns></returns>
        Task<AppClientRegistrationResponse> RegisterClient(ByteArrayId appId, byte[] clientPublicKey, string friendlyName);

        Task<AppClientRegistrationResponse> RegisterChatClient_Temp(ByteArrayId appId, string friendlyName);

        Task<List<RegisteredAppClientResponse>> GetRegisteredClients();
    }
}