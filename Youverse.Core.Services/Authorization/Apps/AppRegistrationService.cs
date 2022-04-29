using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using AppContext = Youverse.Core.Services.Base.AppContext;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private const string AppRegistrationStorageName = "ars";
        private const string AppClientRegistrationStorageName = "adrs";
        private const string AppRsaKeyList = "arsa";

        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, IDriveService driveService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
        }

        public async Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, Guid driveAlias, Guid driveType, string driveName, string driveMetadata, bool createDrive = false, bool canManageConnections = false, bool allowAnonymousReadsToDrive = false)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();
            Guard.Argument(applicationId, nameof(applicationId)).Require(applicationId != Guid.Empty);

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var masterKeyEncryptedAppKey = new SymmetricKeyEncryptedAes(ref masterKey);

            AppDriveGrant defaultDriveGrant = null;
            if (createDrive)
            {
                Guard.Argument(driveAlias, nameof(driveAlias)).NotEqual(Guid.Empty);
                defaultDriveGrant = await this.CreateOwnedDriveInternal(driveAlias, driveName, driveType, driveMetadata, allowAnonymousReadsToDrive, masterKeyEncryptedAppKey);
            }

            const int maxKeys = 4; //leave this size 
            var appKey = masterKeyEncryptedAppKey.DecryptKeyClone(ref masterKey);
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref appKey, maxKeys);
            appKey.Wipe();

            rsaKeyList.Id = applicationId;
            _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(AppRsaKeyList, s => s.Save(rsaKeyList));

            //

            var appReg = new AppRegistration()
            {
                ApplicationId = applicationId,
                Name = name,
                MasterKeyEncryptedAppKey = masterKeyEncryptedAppKey,
                DefaultDriveId = defaultDriveGrant?.DriveId,
                OwnedDrives = defaultDriveGrant == null ? new List<AppDriveGrant>() : new List<AppDriveGrant> {defaultDriveGrant},
                CanManageConnections = canManageConnections,
                AdditionalDriveGrants = new List<AppDriveGrant>()
            };

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            //

            return this.ToAppRegistrationResponse(appReg);
        }

        public async Task CreateOwnedDrive(Guid appId, Guid driveAlias, string driveName, Guid type, string metadata, bool allowAnonymousReads = false)
        {
            Guard.Argument(appId, nameof(appId)).NotEqual(Guid.Empty);
            Guard.Argument(driveAlias, nameof(driveAlias)).NotEqual(Guid.Empty);
            Guard.Argument(driveName, nameof(driveName)).NotEmpty().NotNull();

            var app = await this.GetAppRegistrationInternal(appId);

            if (null == app)
            {
                throw new MissingDataException("App not found");
            }

            //already exists
            if (app.OwnedDrives.Exists(apg => apg.DriveAlias == driveAlias))
            {
                return;
            }

            var grant = await this.CreateOwnedDriveInternal(driveAlias, driveName, type, metadata, allowAnonymousReads, app.MasterKeyEncryptedAppKey);

            app.OwnedDrives.Add(grant);

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(app));
        }

        private async Task<AppDriveGrant> CreateOwnedDriveInternal(Guid driveAlias, string driveName, Guid type, string metadata, bool allowAnonymousReads, SymmetricKeyEncryptedAes masterKeyEncryptedAppKey)
        {
            Guard.Argument(driveAlias, nameof(driveAlias)).NotEqual(Guid.Empty);
            Guard.Argument(driveName, nameof(driveName)).NotEmpty().NotNull();

            var drive = await _driveService.CreateDrive(driveName, type, driveAlias, metadata, allowAnonymousReads);

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var storageKey = drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(ref masterKey);
            var appKey = masterKeyEncryptedAppKey.DecryptKeyClone(ref masterKey);
            var appEncryptedStorageKey = new SymmetricKeyEncryptedAes(ref appKey, ref storageKey);

            //TODO: the drive alias is both on the appdrive grant and the drive.  need to refactor this when I refactor the drive ownership
            return new AppDriveGrant()
            {
                DriveAlias = driveAlias,
                DriveId = drive.Id,
                AppKeyEncryptedStorageKey = appEncryptedStorageKey,
                Permissions = DrivePermissions.All
            };
        }

        public Task RefreshAppKeys()
        {
            //this.GetRsaKeyList()
            //TODO: michael to build a function
            //RsaKeyListManagement.GetCurrentKey(apk, rsaKeyList, out var wasUpdated);
            return Task.CompletedTask;
        }

        public async Task<AppRegistrationResponse> GetAppRegistration(Guid applicationId)
        {
            var result = await GetAppRegistrationInternal(applicationId);
            return ToAppRegistrationResponse(result);
        }

        public async Task<AppContext> GetAppContext(Guid token, SensitiveByteArray clientHalfKek, bool failIfRevoked = false)
        {
            var appClient = await this.GetClientRegistration(token);
            var appReg = await this.GetAppRegistrationInternal(appClient.ApplicationId);

            if (failIfRevoked && appReg.IsRevoked)
            {
                throw new YouverseSecurityException("App is not registered or revoked");
            }

            return new AppContext(
                appId: appReg.ApplicationId,
                appClientId: appClient.Id,
                clientSharedSecret: new SensitiveByteArray(appClient.SharedSecretKey),
                defaultDriveId: appReg.DefaultDriveId,
                hostHalfAppKey: appClient.ServerHalfAppKey,
                clientHalfAppKey: clientHalfKek,
                ownedDrives: appReg.OwnedDrives,
                canManageConnections: appReg.CanManageConnections
            );
        }

        public async Task<AppContextBase> GetAppContextBase(Guid appId, bool includeMasterKey = false, bool failIfRevoked = false)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);

            if (appReg == null)
            {
                return null;
            }

            if (failIfRevoked && appReg.IsRevoked)
            {
                throw new YouverseSecurityException("App is not registered or revoked");
            }

            //TODO: Update owned drives to the drive alias from the drive service

            return new AppContextBase(
                appId: appId,
                appClientId: Guid.Empty,
                clientSharedSecret: null,
                defaultDriveId: appReg.DefaultDriveId.GetValueOrDefault(),
                ownedDrives: appReg.OwnedDrives,
                canManageConnections: appReg.CanManageConnections,
                masterKeyEncryptedAppKey: includeMasterKey ? appReg.MasterKeyEncryptedAppKey : null);
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
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(applicationId);

            if (null == appReg || appReg.IsRevoked)
            {
                throw new YouverseSecurityException($"Application with Id {applicationId} is not registered or has been revoked.");
            }

            //Note: never store clientAppToken

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var appKey = appReg.MasterKeyEncryptedAppKey.DecryptKeyClone(ref masterKey);

            var clientEncryptedAppKey = new SymmetricKeyEncryptedXor(ref appKey, out var clientKek);

            //TODO: encrypt shared secret using the appkey
            var sharedSecret = ByteArrayUtil.GetRndByteArray(16);

            var appClientReg = new AppClientRegistration()
            {
                Id = Guid.NewGuid(),
                ApplicationId = applicationId,
                SharedSecretKey = sharedSecret,
                ServerHalfAppKey = clientEncryptedAppKey,
                IsRevoked = false
            };

            _systemStorage.WithTenantSystemStorage<AppClientRegistration>(AppClientRegistrationStorageName, s => s.Save(appClientReg));

            using (var data = ByteArrayUtil.Combine(clientKek.GetKey(), sharedSecret).ToSensitiveByteArray())
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

        public async Task<TransitPublicKey> GetTransitPublicKey(Guid appId)
        {
            var rsaKeyList = await this.GetRsaKeyList(appId);
            var key = RsaKeyListManagement.GetCurrentKey(ref rsaKeyList, out var keyListWasUpdated);

            if (keyListWasUpdated)
            {
                _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(AppRsaKeyList, s => s.Save(rsaKeyList));
            }

            return new TransitPublicKey
            {
                AppId = appId,
                PublicKeyData = key,
            };
        }

        public async Task<bool> IsValidPublicKey(Guid appId, uint crc)
        {
            var rsaKeyList = await this.GetRsaKeyList(appId);
            var key = RsaKeyListManagement.FindKey(rsaKeyList, crc);
            return null != key;
        }

        public async Task<RsaFullKeyListData> GetRsaKeyList(Guid appId)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<RsaFullKeyListData>(AppRsaKeyList, s => s.Get(appId));

            Guard.Argument(result, "App public private keys").NotNull();

            return result;
        }

        private AppRegistrationResponse ToAppRegistrationResponse(AppRegistration appReg)
        {
            if (appReg == null)
            {
                return null;
            }

            //NOTE: we're not sharing the encrypted app dek, this is crucial
            return new AppRegistrationResponse()
            {
                ApplicationId = appReg.ApplicationId,
                Name = appReg.Name,
                DefaultDriveId = appReg.DefaultDriveId,
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