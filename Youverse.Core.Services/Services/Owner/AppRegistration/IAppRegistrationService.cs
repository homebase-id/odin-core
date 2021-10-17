using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Services.Owner.Authentication;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Owner.AppRegistration
{

    public class AppRegistration
    {
        public Guid ApplicationId { get; set; }
        public string Name { get; set; }
        public Guid Id { get; internal set; }
    }

    public interface IAppRegistrationService
    {
        Task RegisterApplication(Guid applicationId, string name);

        Task<AppRegistration> GetRegistration(Guid applicationId);

    }

    public class AppRegistrationService : DotYouServiceBase, IAppRegistrationService
    {
        private const string AppRegistrationStorageName = "ars";
        private readonly IOwnerAuthenticationService _authenticationService;

        public AppRegistrationService(DotYouContext context, ILogger logger, IOwnerAuthenticationService authenticationService,IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, notificationHub, fac)
        {
            this._authenticationService = authenticationService;
        }

        public Task<AppRegistration> GetRegistration(Guid applicationId)
        {
            var result = WithTenantStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a=>a.ApplicationId == applicationId));
            return result;

        }

        public Task RegisterApplication(Guid applicationId, string name)
        {
            AssertCallerIsOwner();

            AppRegistrationData ard = AppRegistrationManager.CreateApplication(this.Context.Caller.GetLoginKek());

            var appReg = new AppRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                Name = name
            };


            WithTenantStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            return Task.CompletedTask;

        }
    }

}