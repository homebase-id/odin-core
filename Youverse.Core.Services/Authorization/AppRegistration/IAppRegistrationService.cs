using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
    public interface IAppRegistrationService
    {
        Task RegisterApplication(Guid applicationId, string name);

        Task<AppRegistration> GetRegistration(Guid applicationId);

    }
}