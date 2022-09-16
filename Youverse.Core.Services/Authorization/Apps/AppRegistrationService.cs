using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly ExchangeGrantService _exchangeGrantService;

        private readonly ByteArrayId _appRegistrationDataType = ByteArrayId.FromString("__app_reg");
        private readonly ThreeKeyValueStorage _appRegistrationValueStorage;

        private readonly ByteArrayId _appClientDataType = ByteArrayId.FromString("__app_client_reg");
        private readonly ThreeKeyValueStorage _appClientValueStorage;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _exchangeGrantService = exchangeGrantService;

            _appRegistrationValueStorage = systemStorage.ThreeKeyValueStorage;
            _appClientValueStorage = systemStorage.ThreeKeyValueStorage;
        }

        public async Task<RedactedAppRegistration> RegisterApp(ByteArrayId appId, string name, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives)
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

            _appRegistrationValueStorage.Upsert(appReg.AppId, ByteArrayId.Empty.Value, _appRegistrationDataType.Value, appReg);

            return appReg.Redacted();
        }

        public async Task<AppClientRegistrationResponse> RegisterClient(ByteArrayId appId, byte[] clientPublicKey, string friendlyName)
        {
            Guard.Argument(appId, nameof(appId)).Require(x => x != Guid.Empty);
            Guard.Argument(clientPublicKey, nameof(clientPublicKey)).NotNull().Require(x => x.Length > 200);
            Guard.Argument(friendlyName, nameof(friendlyName)).NotNull().NotEmpty();

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (appReg == null)
            {
                throw new YouverseException("App must be registered to add a client");
            }

            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, _contextAccessor.GetCurrent().Caller.GetMasterKey());

            var appClient = new AppClient(appId, accessRegistration);
            _appClientValueStorage.Upsert(accessRegistration.Id, appReg.AppId.Value, _appClientDataType.Value, appClient);

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

        public async Task<AppClientRegistrationResponse> RegisterChatClient_Temp(ByteArrayId appId, string friendlyName)
        {
            Guard.Argument(appId, nameof(appId)).Require(x => x != Guid.Empty);
            Guard.Argument(friendlyName, nameof(friendlyName)).NotNull().NotEmpty();

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (appReg == null)
            {
                throw new YouverseException("App must be registered to add a client");
            }

            var (reg, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, _contextAccessor.GetCurrent().Caller.GetMasterKey());
            var appClient = new AppClient(appId, reg);
            _appClientValueStorage.Upsert(reg.Id, appReg.AppId.Value, _appClientDataType.Value, appClient);

            //RSA encrypt using the public key and send to client - temp removed for early chat client development
            var tokenBytes = cat.ToAuthenticationToken().ToPortableBytes();
            var sharedSecret = cat.SharedSecret.GetKey();

            return new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = ByteArrayUtil.Combine(tokenBytes, sharedSecret)
            };
        }

        public async Task<RedactedAppRegistration> GetAppRegistration(ByteArrayId appId)
        {
            var result = await GetAppRegistrationInternal(appId);
            return result?.Redacted();
        }

        public async Task<(ByteArrayId appId, PermissionContext permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken)
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

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, grantDictionary, accessReg, _contextAccessor.GetCurrent().Caller.IsOwner);
            // var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, appReg.Grant, accessReg, _contextAccessor.GetCurrent().Caller.IsOwner);
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

        public async Task RevokeApp(ByteArrayId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }

            //TODO: revoke all clients? or is the one flag enough?
            _appRegistrationValueStorage.Upsert(appId, ByteArrayId.Empty.Value, _appRegistrationDataType.Value, appReg);
        }

        public async Task RemoveAppRevocation(ByteArrayId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            _appRegistrationValueStorage.Upsert(appId, ByteArrayId.Empty.Value, _appRegistrationDataType.Value, appReg);
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients()
        {
            var list = _systemStorage.ThreeKeyValueStorage.GetByKey3<AccessRegistration>(_appClientDataType);
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

        private async Task<AppRegistration> GetAppRegistrationInternal(ByteArrayId appId)
        {
            var appReg = _appRegistrationValueStorage.Get<AppRegistration>(appId);
            return appReg;
        }
    }
}