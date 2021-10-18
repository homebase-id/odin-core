using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
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