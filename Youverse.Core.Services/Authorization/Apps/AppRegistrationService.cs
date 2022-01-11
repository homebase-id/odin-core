﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private const string AppRegistrationStorageName = "ars";
        private const string AppClientRegistrationStorageName = "adrs";

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
                grants.Add(new DriveGrant() {DriveId = drive.Id, AppKeyEncryptedStorageKey = appEncryptedStorageKey});
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

        public async Task<AppContext> GetAppContext(Guid token, SensitiveByteArray deviceSecret)
        {
            var appClient = await this.GetClientRegistration(token);
            var appReg = await this.GetAppRegistrationInternal(appClient.ApplicationId);

            return new AppContext(
                appId: appReg.ApplicationId.ToString(),
                appClientId: appClient.Id,
                clientSharedSecret: new SensitiveByteArray(appClient.SharedSecretKey),
                driveId: appReg.DriveId,
                encryptedAppKey: appClient.EncryptedAppKey,
                deviceSecret: deviceSecret,
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

        public async Task<AppClientRegistrationResponse> RegisterClient(Guid applicationId, byte[] clientPublicKey)
        {
            _context.Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(applicationId);

            if (null == appReg || appReg.IsRevoked)
            {
                throw new YouverseSecurityException($"Application with Id {applicationId} is not registered or has been revoked.");
            }

            //Note: never store clientAppToken

            var masterKey = _context.Caller.GetMasterKey();
            var appKey = appReg.MasterKeyEncryptedAppKey.DecryptKey(masterKey);

            var clientEncryptedAppKey = new SymmetricKeyEncryptedXor(appKey, out var clientKek);

            //TODO: encrypt shared secret using the appkey
            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

            var appClientReg = new AppClientRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                SharedSecretKey = sharedSecret,
                EncryptedAppKey = clientEncryptedAppKey,
                IsRevoked = false
            };

            _systemStorage.WithTenantSystemStorage<AppClientRegistration>(AppClientRegistrationStorageName, s => s.Save(appClientReg));

            using (var data = ByteArrayUtil.Combine(clientKek, sharedSecret).ToSensitiveByteArray())
            {
                var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
                var encryptedData = publicKey.Encrypt(data.GetKey());

                return new AppClientRegistrationResponse()
                {
                    EncryptionVersion = 1,
                    Token = appClientReg.Id,
                    Data = encryptedData
                };
            }
        }

        public async Task<AppClientRegistration> GetClientRegistration(Guid id)
        {
            var clientReg = await _systemStorage.WithTenantSystemStorageReturnSingle<AppClientRegistration>(AppClientRegistrationStorageName, s => s.Get(id));
            return clientReg;
        }

        public async Task<PagedResult<AppClientRegistration>> GetClientRegistrationList(PageOptions pageOptions)
        {
            var list = await _systemStorage.WithTenantSystemStorageReturnList<AppClientRegistration>(AppClientRegistrationStorageName, s => s.GetList(pageOptions));
            return list;
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