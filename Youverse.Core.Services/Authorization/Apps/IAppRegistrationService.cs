using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Authorization.Apps
{
    public interface IAppRegistrationService
    {
        /// <summary>
        /// Registers an application to be used with this host.  Returns the record Id of the newly registered app
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        Task<Guid> RegisterApp(Guid applicationId, string name);

        Task<AppRegistration> GetAppRegistration(Guid applicationId);

        /// <summary>
        /// Gets all registered apps
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<AppRegistration>> GetRegisteredApps(PageOptions pageOptions);

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
        /// Gets all app registrations for a given device.
        /// </summary>
        /// <param name="uniqueDeviceId"></param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<AppDeviceRegistration>> GetAppsByDevice(byte[] uniqueDeviceId, PageOptions pageOptions);

        /// <summary>
        /// Registers an application on a given device.  Returns the information required by the device
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="uniqueDeviceId"></param>
        /// <param name="sharedSecret"></param>>
        /// <returns></returns>
        Task<AppDeviceRegistrationReply> RegisterAppOnDevice(Guid applicationId, byte[] uniqueDeviceId, byte[] sharedSecret);

        /// <summary>
        /// Returns the specified app device registration
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="uniqueDeviceId"></param>
        /// <returns></returns>
        Task<AppDeviceRegistration> GetAppDeviceRegistration(Guid applicationId, byte[] uniqueDeviceId);

        /// <summary>
        /// Gets the list of devices on which an app is registered.
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<AppDeviceRegistration>> GetRegisteredAppDevices(PageOptions pageOptions);

        /// <summary>
        /// Revokes an specific application on a specific device from accessing this host and its data.
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="uniqueDeviceId"></param>
        /// <returns></returns>
        Task RevokeAppDevice(Guid applicationId, byte[] uniqueDeviceId);
        
        /// <summary>
        /// Revokes an specific application on a specific device from accessing this host and its data.
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="uniqueDeviceId"></param>
        /// <returns></returns>
        Task RemoveAppDeviceRevocation(Guid applicationId, byte[] uniqueDeviceId);
    }
}   