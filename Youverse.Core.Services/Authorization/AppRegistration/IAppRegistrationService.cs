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
    }
}