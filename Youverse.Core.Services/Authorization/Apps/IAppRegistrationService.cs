﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        // Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, Guid driveAlias, Guid driveType, string driveName, string driveMetadata, bool createDrive = false, bool canManageConnections = false, bool allowAnonymousReadsToDrive = false);

        Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, PermissionSet permissions, List<Guid> driveIds);

        Task<AppRegistrationResponse> GetAppRegistration(Guid applicationId);

        Task<AppRegistrationResponse> GetAppRegistrationByGrant(Guid grantId);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<AppRegistrationResponse>> GetRegisteredApps(PageOptions pageOptions);

        /// <summary>
        /// Removes access for a given application across all devices
        /// </summary>
        /// <param name="applicationId"></param>
        /// <returns></returns>
        Task RevokeApp(Guid applicationId);

        /// <summary>
        /// Allows an app that has been revoked
        /// </summary>
        /// <param name="applicationId"></param>
        /// <returns></returns>
        Task RemoveAppRevocation(Guid applicationId);

        //Note: apps will also have their own keystore.  it will store the keys of other apps to which it has access
        Task GetAppKeyStore();

        /// <summary>
        /// Registers an application on a given device.  Returns the information required by the device
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="clientPublicKey">The RSA public key generated by the client; to be used to encrypt the response</param>
        /// >
        /// <returns></returns>
        Task<AppClientRegistrationResponse> RegisterClient(Guid applicationId, byte[] clientPublicKey);
    }
}