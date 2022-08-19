﻿using System;
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
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;
        private readonly ExchangeGrantService _exchangeGrantService;

        private readonly ByteArrayId _appRegistrationDataType = ByteArrayId.FromString("__app_reg");
        private readonly ThreeKeyStorage _appRegistrationStorage;

        private readonly ByteArrayId _appClientDataType = ByteArrayId.FromString("__app_client_reg");
        private readonly ThreeKeyStorage _appClientStorage;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _exchangeGrantService = exchangeGrantService;

            _appRegistrationStorage = systemStorage.KeyValueStorage.ThreeKeyStorage2;
            _appClientStorage = systemStorage.KeyValueStorage.ThreeKeyStorage2;
        }

        public async Task<AppRegistrationResponse> RegisterApp(ByteArrayId appId, string name, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives)
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

            _appRegistrationStorage.Upsert(appReg.AppId.Value, ByteArrayId.Empty.Value, _appRegistrationDataType.Value, appReg);

            return this.ToAppRegistrationResponse(appReg);
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
            _appClientStorage.Upsert(accessRegistration.Id.Value, appReg.AppId.Value, _appClientDataType.Value, appClient);

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
            _appClientStorage.Upsert(reg.Id.Value, appReg.AppId.Value, _appClientDataType.Value, appClient);

            //RSA encrypt using the public key and send to client - temp removed for early chat client development
            var data = ByteArrayUtil.Combine(cat.Id.ToByteArray(), cat.AccessTokenHalfKey.GetKey(), cat.SharedSecret.GetKey()).ToSensitiveByteArray();

            return new AppClientRegistrationResponse()
            {
                EncryptionVersion = 72,
                Token = cat.Id,
                Data = data.GetKey()
            };
        }

        public async Task<AppRegistrationResponse> GetAppRegistration(ByteArrayId appId)
        {
            var result = await GetAppRegistrationInternal(appId);
            return ToAppRegistrationResponse(result);
        }

        public async Task<(Guid appId, PermissionContext permissionContext)> GetPermissionContext(ClientAuthenticationToken authToken)
        {
            var (isValid, accessReg, appReg) = await this.ValidateClientAuthToken(authToken);

            if (!isValid)
            {
                throw new YouverseSecurityException("Invalid token");
            }

            var permissionCtx = await _exchangeGrantService.CreatePermissionContext(authToken, appReg.Grant, accessReg, _contextAccessor.GetCurrent().Caller.IsOwner);
            return (appReg.AppId, permissionCtx);
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(ClientAuthenticationToken authToken)
        {
            var appClient = _appClientStorage.Get<AppClient>(authToken.Id.ToByteArray());
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
            _appRegistrationStorage.Upsert(appId.Value, ByteArrayId.Empty.Value, _appRegistrationDataType.Value, appReg);
        }

        public async Task RemoveAppRevocation(ByteArrayId appId)
        {
            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            _appRegistrationStorage.Upsert(appId.Value, ByteArrayId.Empty.Value, _appRegistrationDataType.Value, appReg);
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients()
        {
            var list = _systemStorage.KeyValueStorage.ThreeKeyStorage2.GetByKey3<AccessRegistration>(_appClientDataType);
            var resp = list.Select(accessReg => new RegisteredAppClientResponse()
            {
                IsRevoked = accessReg.IsRevoked,
                Created = accessReg.Created,
                AccessRegistrationClientType = accessReg.AccessRegistrationClientType
            }).ToList();

            return resp;
        }

        public Task<List<AppRegistrationResponse>> GetRegisteredApps()
        {
            var apps = _appRegistrationStorage.GetByKey3<AppRegistration>(_appRegistrationDataType);
            var redactedList = apps.Select(ToAppRegistrationResponse).ToList();
            return Task.FromResult(redactedList);
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
                AppId = appReg.AppId,
                Name = appReg.Name,
                IsRevoked = appReg.Grant.IsRevoked,
                Created = appReg.Grant.Created,
                Modified = appReg.Grant.Modified
            };
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(ByteArrayId appId)
        {
            var appReg = _appRegistrationStorage.Get<AppRegistration>(appId);
            return appReg;
        }
    }
}