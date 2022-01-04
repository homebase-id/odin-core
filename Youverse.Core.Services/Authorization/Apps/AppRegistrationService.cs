using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private const string AppRegistrationStorageName = "ars";
        private const string AppDeviceRegistrationStorageName = "adrs";

        private readonly DotYouContext _context;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;

        public AppRegistrationService(DotYouContext context, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, IDriveService driveService)
        {
            _context = context;
            _systemStorage = systemStorage;
            _driveService = driveService;
        }

        public async Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, bool createDrive = false)
        {
            Guard.Argument(applicationId, nameof(applicationId)).Require(applicationId != Guid.Empty);
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();

            _context.Caller.AssertHasMasterKey();

            Guid? driveId = null;

            var masterKey = _context.Caller.GetMasterKey();
            var appKey = new SymmetricKeyEncryptedAes(masterKey);
            
            List<DriveGrant> grants = null;

            if (createDrive)
            {
                var drive = await _driveService.CreateDrive($"{name}-drive");
                var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKey(masterKey);

                var appEncryptedStorageKey = new SymmetricKeyEncryptedAes(appKey.DecryptKey(masterKey), ref storageKey);

                grants = new List<DriveGrant>();
                grants.Add(new DriveGrant() { DriveId = drive.Id, AppKeyEncryptedStorageKey = appEncryptedStorageKey });
                driveId = drive.Id;
            }

            var appReg = new AppRegistration()
            {
                ApplicationId = applicationId,
                Name = name,
                MasterKeyEncryptedAppKey = appKey,
                DriveId = driveId,
                DriveGrants = grants
            };

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            return this.ToAppRegistrationResponse(appReg);
        }

        public async Task<AppRegistrationResponse> GetAppRegistration(Guid applicationId)
        {
            var result = await GetAppRegistrationInternal(applicationId);
            return ToAppRegistrationResponse(result);
        }

        public async Task<AppContext> GetAppContext(Guid applicationId, byte[] deviceUid, SensitiveByteArray deviceSecret)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            var deviceReg = await this.GetAppDeviceRegistration(applicationId, deviceUid);

            //var appEncryptionKey = deviceReg.EncryptedAppKey.DecryptKey(deviceSecret);


            return new AppContext(
                appId: appReg.ApplicationId.ToString(),
                deviceUid: deviceUid,
                deviceSharedSecret: new SensitiveByteArray(deviceReg.SharedSecret),
                driveId: appReg.DriveId,
                encryptedAppKey: deviceReg.EncryptedAppKey,
                deviceSecret:deviceSecret,
                driveGrants: appReg.DriveGrants);
        }

        public async Task RevokeApp(Guid applicationId)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            if (null != appReg)
            {
                appReg.IsRevoked = true;
                _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));
            }

            //TODO: do we do anything with storage DEK here?

            //TODO: Send notification?
        }

        public async Task RemoveAppRevocation(Guid applicationId)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            if (null != appReg)
            {
                appReg.IsRevoked = false;
                _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));
            }

            //TODO: do we do anything with storage DEK here?

            //TODO: Send notification?
        }

        public async Task GetAppKeyStore()
        {
            throw new NotImplementedException();
        }

        public async Task<AppDeviceRegistrationResponse> RegisterDevice(Guid applicationId, byte[] uniqueDeviceId, byte[] sharedSecret)
        {
            _context.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(applicationId);

            if (null == appReg || appReg.IsRevoked)
            {
                throw new InvalidDataException($"Application with Id {applicationId} is not registered or has been revoked.");
            }

            var masterKey = _context.Caller.GetMasterKey();
            var appKey = appReg.MasterKeyEncryptedAppKey.DecryptKey(masterKey);
            var (clientAppToken, serverRegData) = AppClientTokenManager.CreateClientToken(appKey, sharedSecret);

            //Note: never store deviceAppToken

            var appDeviceReg = new AppDeviceRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                UniqueDeviceId = uniqueDeviceId,
                SharedSecret = sharedSecret,
                EncryptedAppKey = serverRegData.DeviceEncryptedDeviceKey,
                IsRevoked = false
            };

            _systemStorage.WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDeviceReg));

            //TODO: i wonder if token could be the deviceUid?
            return new AppDeviceRegistrationResponse()
            {
                Token = appDeviceReg.Id,
                DeviceAppKey = clientAppToken
            };
        }

        public async Task<AppDeviceRegistration> GetAppDeviceRegistration(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDeviceReg = await _systemStorage.WithTenantSystemStorageReturnSingle<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId && a.UniqueDeviceId == uniqueDeviceId));
            return appDeviceReg;
        }

        public async Task<PagedResult<AppDeviceRegistration>> GetRegisteredAppDevices(PageOptions pageOptions)
        {
            var list = await _systemStorage.WithTenantSystemStorageReturnList<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.GetList(pageOptions));
            return list;
        }

        public async Task<PagedResult<AppDeviceRegistration>> GetAppsByDevice(byte[] uniqueDeviceId, PageOptions pageOptions)
        {
            var list = await _systemStorage.WithTenantSystemStorageReturnList<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Find(ad => ad.UniqueDeviceId == uniqueDeviceId, pageOptions));
            return list;
        }

        public async Task RevokeAppDevice(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDevice = await this.GetAppDeviceRegistration(applicationId, uniqueDeviceId);
            appDevice.IsRevoked = true;
            _systemStorage.WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDevice));
        }

        public async Task RemoveAppDeviceRevocation(Guid applicationId, byte[] uniqueDeviceId)
        {
            var appDevice = await this.GetAppDeviceRegistration(applicationId, uniqueDeviceId);
            appDevice.IsRevoked = false;
            _systemStorage.WithTenantSystemStorage<AppDeviceRegistration>(AppDeviceRegistrationStorageName, s => s.Save(appDevice));
        }

        public async Task<PagedResult<AppRegistrationResponse>> GetRegisteredApps(PageOptions pageOptions)
        {
            var apps = await _systemStorage.WithTenantSystemStorageReturnList<AppRegistration>(AppRegistrationStorageName, s => s.GetList(pageOptions));
            var redactedList = apps.Results.Select(ToAppRegistrationResponse).ToList();
            return new PagedResult<AppRegistrationResponse>(pageOptions, apps.TotalPages, redactedList);
        }

        private AppRegistrationResponse ToAppRegistrationResponse(AppRegistration appReg)
        {
            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new AppRegistrationResponse()
            {
                ApplicationId = appReg.ApplicationId,
                Name = appReg.Name,
                DriveId = appReg.DriveId,
                IsRevoked = appReg.IsRevoked
            };
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(Guid applicationId)
        {
            
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId));
            return result;
        }
    }
}