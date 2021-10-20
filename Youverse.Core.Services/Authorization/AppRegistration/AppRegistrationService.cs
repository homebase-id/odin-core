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
        private const string AppDeviceRegistrationStorageName = "adrs";

        public AppRegistrationService(DotYouContext context, ILogger logger, IOwnerAuthenticationService authenticationService, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context,
            logger, notificationHub, fac)
        {
        }

        public Task<AppRegistration> GetAppRegistration(Guid applicationId)
        {
            var result = WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId));
            return result;
        }

        public async Task RevokeApp(Guid applicationId)
        {
            var appReg = await this.GetAppRegistration(applicationId);
            if (null != appReg)
            {
                WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Delete(appReg.Id));
            }

            //TODO: Send notification?
        }

        public async Task GetAppKeyStore()
        {
            throw new NotImplementedException();
        }

        public Task<Guid> RegisterApp(Guid applicationId, string name)
        {
            AssertCallerIsOwner();

            //TODO: apps cannot access this method
            //AssertCallerIsNotApp();

            AppEncryptionKey key = AppRegistrationManager.CreateAppKey(this.Context.Caller.GetLoginDek().GetKey());

            var appReg = new AppRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                Name = name,
                AppIV = key.AppIV,
                EncryptedAppDeK = key.EncryptedAppDeK
            };

            WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            return Task.FromResult(appReg.Id);
        }

        public async Task<AppDeviceRegistrationReply> RegisterAppOnDevice(Guid applicationId, byte[] uniqueDeviceId, byte[] sharedSecret)
        {
            var savedApp = await this.GetAppRegistration(applicationId);

            var appEnc = new AppEncryptionKey()
            {
                AppIV = savedApp.AppIV,
                EncryptedAppDeK = savedApp.EncryptedAppDeK
            };

            var decryptedAppDek = AppRegistrationManager.GetApplicationDekWithLogin(appEnc, this.Context.Caller.GetLoginDek());

            var (deviceAppToken, appRegData) = AppClientTokenManager.CreateClientToken(decryptedAppDek.GetKey(), sharedSecret);
            decryptedAppDek.Wipe();

            //Note: never store deviceAppToken

            var appDeviceReg = new AppDeviceRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                UniqueDeviceId = uniqueDeviceId,
                SharedSecret = sharedSecret,
                HalfAdek = appRegData.halfAdek,
                IsRevoked = false
            };

            this.WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDeviceReg));

            return new AppDeviceRegistrationReply()
            {
                Id = appDeviceReg.Id,
                DeviceAppToken = deviceAppToken,
                SharedSecret = sharedSecret
            };
        }

        public async Task<AppDeviceRegistration> GetRegisteredAppDevice(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDeviceReg = await WithTenantSystemStorageReturnSingle<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId && a.UniqueDeviceId == uniqueDeviceId));
            return appDeviceReg;
        }

        public async Task<PagedResult<AppDeviceRegistration>> GetRegisteredAppDevices(PageOptions pageOptions)
        {
            var list = await WithTenantSystemStorageReturnList<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.GetList(pageOptions));
            return list;
        }

        public async Task RevokeDevice(byte[] uniqueDeviceId)
        {
            WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.DeleteMany(ad => ad.UniqueDeviceId == uniqueDeviceId));
        }

        public async Task RevokeAppDevice(Guid applicationId, byte[] uniqueDeviceId)
        {
            WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.DeleteMany(ad => ad.ApplicationId == applicationId && ad.UniqueDeviceId == uniqueDeviceId));
        }

        public async Task<PagedResult<AppRegistration>> GetRegisteredApps(PageOptions pageOptions)
        {
            var apps = await WithTenantSystemStorageReturnList<AppRegistration>(AppRegistrationStorageName, s => s.GetList(pageOptions));
            return apps;
        }

        public async Task<AppDeviceRegistration> GetDeviceAppRegistration(Guid deviceRegistrationId)
        {
            var appDeviceReg = await WithTenantSystemStorageReturnSingle<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Get(deviceRegistrationId));
            return appDeviceReg;
        }


    }
}