using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrantRedux;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private const string AppRegistrationStorageName = "ars";
        private const string AppAccessTokenReg = "appatr";

        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly IDriveService _driveService;
        private readonly ExchangeGrantServiceRedux _exchangeGrantService;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, IDriveService driveService,
            ExchangeGrantServiceRedux exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _driveService = driveService;
            _exchangeGrantService = exchangeGrantService;
        }

        public async Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, PermissionSet permissions, List<TargetDrive> drives)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();
            Guard.Argument(applicationId, nameof(applicationId)).Require(applicationId != Guid.Empty);

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.CreateExchangeGrant(permissions, drives, masterKey);

            var appReg = new AppRegistration()
            {
                ApplicationId = applicationId,
                Name = name,
                Grant = grant
            };

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            return this.ToAppRegistrationResponse(appReg);
        }

        public async Task<AppClientRegistrationResponse> RegisterClient(Guid applicationId, byte[] clientPublicKey)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(applicationId);
            var (reg, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant);

            reg.GrantId = applicationId;

            _systemStorage.WithTenantSystemStorage<AccessRegistration>(AppAccessTokenReg, s => s.Save(reg));

            //RSA encrypt using the public key and send to client
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

        public async Task<(Guid appId, PermissionContext permissionContext)> GetAppExchangeGrant(ClientAuthenticationToken authToken)
        {
            var accessRegistration = await _systemStorage.WithTenantSystemStorageReturnSingle<AccessRegistration>(AppAccessTokenReg, s => s.Get(authToken.Id));
            var appReg = await this.GetAppRegistrationInternal(accessRegistration.GrantId);

            if (null == accessRegistration || null == appReg || null == appReg.Grant)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            if (accessRegistration.IsRevoked || appReg.Grant.IsRevoked)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            //TODO: Need to decide if we store shared secret clear text or decrypt just in time.
            var key = authToken.AccessTokenHalfKey;
            var accessKey = accessRegistration.ClientAccessKeyEncryptedKeyStoreKey.DecryptKeyClone(ref key);
            var sharedSecret = accessRegistration.AccessKeyStoreKeyEncryptedSharedSecret.DecryptKeyClone(ref accessKey);

            var grantKeyStoreKey = accessRegistration.GetGrantKeyStoreKey(accessKey);
            accessKey.Wipe();

            var permissionCtx = new PermissionContext(
                driveGrants: appReg.Grant.KeyStoreKeyEncryptedDriveGrants,
                permissionSet: appReg.Grant.PermissionSet,
                driveDecryptionKey: grantKeyStoreKey,
                sharedSecretKey: sharedSecret,
                exchangeGrantId: accessRegistration.GrantId,
                accessRegistrationId: accessRegistration.Id,
                isOwner: _contextAccessor.GetCurrent().Caller.IsOwner
            );

            return (appReg.ApplicationId, permissionCtx);
        }
        
        public async Task RevokeApp(Guid applicationId)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }
            
            //TODO: revoke all clients? or is the one flag enough?

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));
        }

        public async Task RemoveAppRevocation(Guid applicationId)
        {
            var appReg = await this.GetAppRegistrationInternal(applicationId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            //save
            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));
        }

        public async Task<PagedResult<RegisteredAppClientResponse>> GetRegisteredClients(PageOptions pageOptions)
        {
            var list = await _systemStorage.WithTenantSystemStorageReturnList<AccessRegistration>(AppAccessTokenReg, s => s.GetList(pageOptions));
            
            var resp = list.Results.Select(accessReg => new RegisteredAppClientResponse()
            {
                IsRevoked = accessReg.IsRevoked,
                Created = accessReg.Created,
                AccessRegistrationClientType = accessReg.AccessRegistrationClientType
            }).ToList();

            return new PagedResult<RegisteredAppClientResponse>(pageOptions, list.TotalPages, resp);
        }

        public async Task<PagedResult<AppRegistrationResponse>> GetRegisteredApps(PageOptions pageOptions)
        {
            var apps = await _systemStorage.WithTenantSystemStorageReturnList<AppRegistration>(AppRegistrationStorageName, s => s.GetList(pageOptions));
            var redactedList = apps.Results.Select(ToAppRegistrationResponse).ToList();
            return new PagedResult<AppRegistrationResponse>(pageOptions, apps.TotalPages, redactedList);
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
                IsRevoked = appReg.Grant.IsRevoked
            };
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(Guid applicationId)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.ApplicationId == applicationId));
            return result;
        }
    }
}