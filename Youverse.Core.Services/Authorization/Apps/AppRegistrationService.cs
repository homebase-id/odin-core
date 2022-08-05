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
        private readonly ExchangeGrantService _exchangeGrantService;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ISystemStorage systemStorage, ExchangeGrantService exchangeGrantService)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
            _exchangeGrantService = exchangeGrantService;

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.EnsureIndex(k => k.AppId));
        }

        public async Task<AppRegistrationResponse> RegisterApp(Guid applicationId, string name, PermissionSet permissions, IEnumerable<DriveGrantRequest> drives)
        {
            Guard.Argument(name, nameof(name)).NotNull().NotEmpty();
            Guard.Argument(applicationId, nameof(applicationId)).Require(applicationId != Guid.Empty);

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.CreateExchangeGrant(permissions, drives, masterKey);

            var appReg = new AppRegistration()
            {
                AppId = applicationId,
                Name = name,
                Grant = grant
            };

            _systemStorage.WithTenantSystemStorage<AppRegistration>(AppRegistrationStorageName, s => s.Save(appReg));

            return this.ToAppRegistrationResponse(appReg);
        }

        public async Task<AppClientRegistrationResponse> RegisterClient(Guid appId, byte[] clientPublicKey, string friendlyName)
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

            var (reg, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, _contextAccessor.GetCurrent().Caller.GetMasterKey());

            reg.GrantId = appId;

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
        
        public async Task<AppClientRegistrationResponse> RegisterChatClient_Temp(Guid appId, string friendlyName)
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

            reg.GrantId = appId;

            _systemStorage.WithTenantSystemStorage<AccessRegistration>(AppAccessTokenReg, s => s.Save(reg));

            //RSA encrypt using the public key and send to client
            var data = ByteArrayUtil.Combine(cat.Id.ToByteArray(), cat.AccessTokenHalfKey.GetKey(), cat.SharedSecret.GetKey()).ToSensitiveByteArray();

            return new AppClientRegistrationResponse()
            {
                EncryptionVersion = 72,
                Token = cat.Id,
                Data = data.GetKey()
            };
        }

        public async Task<AppRegistrationResponse> GetAppRegistration(Guid appId)
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
            var accessRegistration = await _systemStorage.WithTenantSystemStorageReturnSingle<AccessRegistration>(AppAccessTokenReg, s => s.Get(authToken.Id));

            if (null == accessRegistration)
            {
                return (false, null, null);
            }

            var appReg = await this.GetAppRegistrationInternal(accessRegistration.GrantId);

            if (null == appReg || null == appReg.Grant)
            {
                return (false, null, null);
            }

            if (accessRegistration.IsRevoked || appReg.Grant.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, accessRegistration, appReg);
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
                AppId = appReg.AppId,
                Name = appReg.Name,
                IsRevoked = appReg.Grant.IsRevoked,
                Created = appReg.Grant.Created,
                Modified = appReg.Grant.Modified
            };
        }

        private async Task<AppRegistration> GetAppRegistrationInternal(Guid applicationId)
        {
            var result = await _systemStorage.WithTenantSystemStorageReturnSingle<AppRegistration>(AppRegistrationStorageName, s => s.FindOne(a => a.AppId == applicationId));
            return result;
        }
    }
}