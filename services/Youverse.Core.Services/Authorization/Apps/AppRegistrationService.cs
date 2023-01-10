using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly ExchangeGrantService _exchangeGrantService;

        private readonly GuidId _appRegistrationDataType = GuidId.FromString("__app_reg");
        private readonly ThreeKeyValueStorage _appRegistrationValueStorage;

        private readonly GuidId _appClientDataType = GuidId.FromString("__app_client_reg");
        private readonly ThreeKeyValueStorage _appClientValueStorage;
        private readonly DotYouContextCache _cache;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ITenantSystemStorage tenantSystemStorage, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _tenantSystemStorage = tenantSystemStorage;
            _exchangeGrantService = exchangeGrantService;

            _appRegistrationValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _appClientValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _cache = new DotYouContextCache();
        }

        public async Task<RedactedAppRegistration> RegisterApp(GuidId appId, string name, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();
            Guard.Argument(appId, nameof(appId)).Require(appId != Guid.Empty);

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.CreateExchangeGrant(permissions, drives, masterKey);

            var appReg = new AppRegistration()
            {
                AppId = appId,
                Name = name,
                Grant = grant
            };

            _appRegistrationValueStorage.Upsert(appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            return appReg.Redacted();
        }

        public async Task<AppClientRegistrationResponse> RegisterClient(GuidId appId, byte[] clientPublicKey, string friendlyName)
        {
            Guard.Argument(appId, nameof(appId)).Require(x => x != Guid.Empty);
            Guard.Argument(clientPublicKey, nameof(clientPublicKey)).NotNull().Require(x => x.Length > 200);
            Guard.Argument(friendlyName, nameof(friendlyName)).NotNull().NotEmpty();

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (appReg == null)
            {
                throw new YouverseClientException("App must be registered to add a client", YouverseClientErrorCode.AppNotRegistered);
            }

            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, _contextAccessor.GetCurrent().Caller.GetMasterKey(), ClientTokenType.Other);

            var appClient = new AppClient(appId, accessRegistration);
            _appClientValueStorage.Upsert(accessRegistration.Id, appReg.AppId, _appClientDataType, appClient);

            //RSA encrypt using the public key and send to client
            var tokenBytes = cat.ToAuthenticationToken().ToPortableBytes();
            var sharedSecret = cat.SharedSecret.GetKey();

            var data = ByteArrayUtil.Combine(tokenBytes, sharedSecret);
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
            var encryptedData = publicKey.Encrypt(data);
            
            data.WriteZeros();

            return new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = encryptedData
            };
        }
        
        public async Task<RedactedAppRegistration> GetAppRegistration(GuidId appId)
        {
            var result = await GetAppRegistrationInternal(appId);
            return result?.Redacted();
        }

        public async Task<(GuidId appId, PermissionContext permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            var (isValid, accessReg, appReg) = await this.ValidateClientAuthToken(authToken);

            if (!isValid)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            var grantDictionary = new Dictionary<string, ExchangeGrant>
            {
                { "app_exchange_grant", appReg.Grant }
            };

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, grantDictionary, accessReg, _contextAccessor.GetCurrent().Caller.IsOwner, includeAnonymousDrives: true);
            return (appReg.AppId, permissionCtx);
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(ClientAuthenticationToken authToken)
        {
            var appClient = _appClientValueStorage.Get<AppClient>(authToken.Id);
            if (null == appClient)
            {
                return (false, null, null);
            }

            var appReg = await this.GetAppRegistrationInternal(appClient.AppId);

            if (null == appReg || null == appReg.Grant)
            {
                return (false, null, null);
            }

            if (appClient.AccessRegistration.IsRevoked || appReg.Grant.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, appClient.AccessRegistration, appReg);
        }

        public async Task RevokeApp(GuidId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }

            //TODO: revoke all clients? or is the one flag enough?
            _appRegistrationValueStorage.Upsert(appId, GuidId.Empty, _appRegistrationDataType, appReg);
        }

        public async Task RemoveAppRevocation(GuidId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            _appRegistrationValueStorage.Upsert(appId, GuidId.Empty, _appRegistrationDataType, appReg);
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients()
        {
            var list = _tenantSystemStorage.ThreeKeyValueStorage.GetByKey3<AccessRegistration>(_appClientDataType);
            var resp = list.Select(accessReg => new RegisteredAppClientResponse()
            {
                IsRevoked = accessReg.IsRevoked,
                Created = accessReg.Created,
                AccessRegistrationClientType = accessReg.AccessRegistrationClientType
            }).ToList();

            return resp;
        }

        public Task<List<RedactedAppRegistration>> GetRegisteredApps()
        {
            var apps = _appRegistrationValueStorage.GetByKey3<AppRegistration>(_appRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return Task.FromResult(redactedList);
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(GuidId appId)
        {
            var appReg = _appRegistrationValueStorage.Get<AppRegistration>(appId);
            return appReg;
        }

        public bool TryGetCachedContext(ClientAuthenticationToken token, out DotYouContext context)
        {
            return _cache.TryGetContext(token, out context);
        }

        public void CacheContext(ClientAuthenticationToken token, DotYouContext dotYouContext)
        {
            _cache.CacheContext(token, dotYouContext);
        }
    }
}
