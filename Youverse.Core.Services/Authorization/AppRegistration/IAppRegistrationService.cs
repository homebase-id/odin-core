using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
    public interface IAppRegistrationService
    {
        Task RegisterApplication(Guid applicationId, string name);

        Task<AppRegistration> GetRegistration(Guid applicationId);

        //Note: apps will also have their own keystore.  it will store the keys of other apps to which it has access
        Task GetAppKeyStore();

        /// <summary>
        /// Registers an application on a given device.  Returns the information required by the device
        /// </summary>
        /// <param name="applicationId"></param>
        /// <param name="uniqueDeviceId"></param>
        /// <param name="sharedSecret"></param>>
        /// <returns></returns>
        Task<AppDeviceRegistrationReply> RegisterAppOnDevice(Guid applicationId, byte[] uniqueDeviceId, byte[] sharedSecret);
    }
}