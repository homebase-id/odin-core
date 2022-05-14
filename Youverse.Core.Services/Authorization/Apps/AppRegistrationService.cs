using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;

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
        private readonly ExchangeGrantService _exchangeGrantService;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, IDriveService driveService, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
            _exchangeGrantService = exchangeGrantService;
        }

        public async Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, PermissionSet permissions, List<Guid> driveIds)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();
            Guard.Argument(applicationId, nameof(applicationId)).Require(applicationId != Guid.Empty);

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            //TODO: need to build a different overload tha does not create client access token
            var (accessRegistration, clientAccessToken) = await _exchangeGrantService.RegisterAppExchangeGrant(applicationId, permissions, driveIds);

            //TODO: start * remove this section when we switch to using shared secrets for the transit encryption
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.GetExchangeGrant(accessRegistration.GrantId);
            var appKey = grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var rsaKeyList = RsaKeyListManagement.CreateRsaKeyList(ref appKey, 4);
            appKey.Wipe();
            rsaKeyList.Id = applicationId;
            _systemStorage.WithTenantSystemStorage<RsaFullKeyListData>(AppRsaKeyList, s => s.Save(rsaKeyList));
            //TODO: end * remove this section when we switch to using shared secrets for the transit encryption

            var appReg = new AppRegistration()
            {
                ApplicationId = applicationId,
                Name = name,
                ExchangeGrantId = accessRegistration.GrantId
            };

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            //

            return this.ToAppRegistrationResponse(appReg);
        }

        public async Task<AppClientRegistrationResponse> RegisterClient(Guid applicationId, byte[] clientPublicKey)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(applicationId);
            var (reg, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.ExchangeGrantId);

            var data = ByteArrayUtil.Combine(cat.Id.ToByteArray(), cat.AccessTokenHalfKey.GetKey(), cat.SharedSecret.GetKey()).ToSensitiveByteArray();
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
            var encryptedData = publicKey.Encrypt(data.GetKey());
            data.Wipe();

            return new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = encryptedData
            };
        }

        public async Task<AppRegistrationResponse> GetAppRegistration(Guid applicationId)
        {
            var result = await GetAppRegistrationInternal(applicationId);
            return ToAppRegistrationResponse(result);
        }

        public async Task<AppRegistrationResponse> GetAppRegistrationByGrant(Guid grantId)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ExchangeGrantId == grantId));
            return ToAppRegistrationResponse(result);
        }

        private async Task<AppRegistration> GetAppRegistrationByGrantId(Guid grantId)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ExchangeGrantId == grantId));
            return result;
        }


        public async Task RevokeApp(Guid applicationId)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            if (null != appReg)
            {
                await _exchangeGrantService.RevokeExchangeGrant(appReg.ExchangeGrantId);
            }

            //TODO: Send notification?
        }

        public async Task RemoveAppRevocation(Guid applicationId)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            if (null != appReg)
            {
                await _exchangeGrantService.RemoveExchangeGrantRevocation(appReg.ExchangeGrantId);
            }

            //TODO: do we do anything with storage DEK here?

            //TODO: Send notification?
        }

        public async Task GetAppKeyStore()
        {
            throw new NotImplementedException();
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
                Name = appReg.Name
            };
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(Guid applicationId)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId));
            return result;
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
    }
}