using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : DotYouServiceBase, IAppRegistrationService
    {
        private const string AppRegistrationStorageName = "ars";
        private const string AppDeviceRegistrationStorageName = "adrs";

        public AppRegistrationService(DotYouContext context, ILogger logger, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) :
            base(context, logger, notificationHub, fac)
        {
        }

        public Task<Guid> RegisterApp(Guid applicationId, string name)
        {
            AssertCallerIsOwner();

            //TODO: apps cannot access this method
            //AssertCallerIsNotApp();

            AppEncryptionKey key = AppRegistrationManager.CreateAppDek(this.Context.Caller.GetLoginDek().GetKey());

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

        public async Task<AppRegistration> GetAppRegistration(Guid applicationId)
        {
            var result = await WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId));
            return result;
        }

        public async Task RevokeApp(Guid applicationId)
        {
            var appReg = await this.GetAppRegistration(applicationId);
            if (null != appReg)
            {
                appReg.IsRevoked = true;
                WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));
            }

            //TODO: Send notification?
        }

        public async Task RemoveAppRevocation(Guid applicationId)
        {
            var appReg = await this.GetAppRegistration(applicationId);
            if (null != appReg)
            {
                appReg.IsRevoked = false;
                WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));
            }

            //TODO: Send notification?
        }

        public async Task GetAppKeyStore()
        {
            throw new NotImplementedException();
        }

        public async Task<AppDeviceRegistrationReply> RegisterAppOnDevice(Guid applicationId, byte[] uniqueDeviceId, byte[] sharedSecret)
        {
            var savedApp = await this.GetAppRegistration(applicationId);

            if (null == savedApp || savedApp.IsRevoked)
            {
                throw new InvalidDataException($"Application with Id {applicationId} is not registered or has been revoked.");
            }

            var appEnc = new AppEncryptionKey()
            {
                AppIV = savedApp.AppIV,
                EncryptedAppDeK = savedApp.EncryptedAppDeK
            };

            var decryptedAppDek = AppRegistrationManager.DecryptAppDekWithLoginDek(appEnc, this.Context.Caller.GetLoginDek());
            var (clientAppToken, serverRegData) = AppClientTokenManager.CreateClientToken(decryptedAppDek.GetKey(), sharedSecret);
            decryptedAppDek.Wipe();

            //Note: never store deviceAppToken

            var appDeviceReg = new AppDeviceRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                UniqueDeviceId = uniqueDeviceId,
                SharedSecret = sharedSecret,
                HalfAdek = serverRegData.halfAdek,
                IsRevoked = false
            };

            this.WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDeviceReg));

            return new AppDeviceRegistrationReply()
            {
                Token = appDeviceReg.Id,
                DeviceAppKey = clientAppToken
            };
        }

        public async Task<AppDeviceRegistration> GetAppDeviceRegistration(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDeviceReg = await WithTenantSystemStorageReturnSingle<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId && a.UniqueDeviceId == uniqueDeviceId));
            return appDeviceReg;
        }

        public async Task<PagedResult<AppDeviceRegistration>> GetRegisteredAppDevices(PageOptions pageOptions)
        {
            var list = await WithTenantSystemStorageReturnList<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.GetList(pageOptions));
            return list;
        }

        public async Task<PagedResult<AppDeviceRegistration>> GetAppsByDevice(byte[] uniqueDeviceId, PageOptions pageOptions)
        {
            var list = await WithTenantSystemStorageReturnList<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Find(ad => ad.UniqueDeviceId == uniqueDeviceId, pageOptions));
            return list;
        }

        public async Task RevokeAppDevice(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDevice = await this.GetAppDeviceRegistration(applicationId, uniqueDeviceId);
            appDevice.IsRevoked = true;
            WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDevice));
        }

        public async Task RemoveAppDeviceRevocation(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDevice = await this.GetAppDeviceRegistration(applicationId, uniqueDeviceId);
            appDevice.IsRevoked = false;
            WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDevice));
        }

        public async Task<PagedResult<AppRegistration>> GetRegisteredApps(PageOptions pageOptions)
        {
            var apps = await WithTenantSystemStorageReturnList<AppRegistration>(AppRegistrationStorageName, s => s.GetList(pageOptions));
            return apps;
        }

    }
}