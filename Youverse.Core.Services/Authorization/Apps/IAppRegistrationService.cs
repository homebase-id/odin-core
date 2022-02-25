﻿using System;
using System.Threading.Tasks;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Transit;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Core.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        //Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, bool createDrive = false, bool canManageConnections = false);
        
        Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, bool createDrive = false, Guid? defaultDrivePublicId = null, bool canManageConnections = false);
        
        Task<AppRegistrationResponse> GetAppRegistration(Guid applicationId);

        /// <summary>
        /// Creates the AppContext for a given client registration
        /// </summary>
        /// <returns></returns>
        Task<AppContext> GetAppContext(Guid token, SensitiveByteArray clientHalfKek);

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
        /// Ensures public/private app keys are valid
        /// </summary>
        /// <returns></returns>
        Task RefreshAppKeys();

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

        /// <summary>
        /// Returns the specified app device registration
        /// </summary>
        /// <returns></returns>
        Task<AppClientRegistration> GetClientRegistration(Guid id);

        /// <summary>
        /// Gets the list of devices on which an app is registered.
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<AppClientRegistration>> GetClientRegistrationList(PageOptions pageOptions);

        Task<TransitPublicKey> GetTransitPublicKey(Guid appId);

        Task<bool> IsValidPublicKey(Guid transitContextAppId, uint publicKeyCrc);

        Task<RsaFullKeyListData> GetRsaKeyList(Guid appId);

        /// <summary>
        /// Creates app context specifically for the transit system
        /// </summary>
        /// <returns></returns>
        Task<AppContextBase> GetAppContextBase(Guid appId, bool includeMasterKey = false);

        /// <summary>
        /// Creates a new drive to be used with the specified app.  
        /// </summary>
        /// <param name="appId">The app Id</param>
        /// <param name="publicDriveIdentifier">A key which can be shared publicly to specify when data should be pulled from this drive</param>
        /// <param name="driveName"></param>
        /// <returns></returns>
        Task CreateOwnedDrive(Guid appId, Guid publicDriveIdentifier, string driveName);
    }
}