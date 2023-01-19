using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
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
        private readonly TenantContext _tenantContext;

        private readonly CircleNetworkService _circleNetworkService;

        public AppRegistrationService(DotYouContextAccessor contextAccessor, ILogger<IAppRegistrationService> logger, ITenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, YouverseConfiguration config, TenantContext tenantContext,
            CircleNetworkService circleNetworkService)
        {
            _contextAccessor = contextAccessor;
            _tenantSystemStorage = tenantSystemStorage;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _circleNetworkService = circleNetworkService;

            _appRegistrationValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _appClientValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _cache = new DotYouContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        public async Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();
            Guard.Argument(request.AppId, nameof(request.AppId)).Require(request.AppId != Guid.Empty);

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var appGrant = await _exchangeGrantService.CreateExchangeGrant(request.PermissionSet, request.Drives, masterKey);

            //TODO: add check to ensure app name is unique
            //TODO: add check if app is already registered
            
            var appReg = new AppRegistration()
            {
                AppId = request.AppId,
                Name = request.Name,
                Grant = appGrant,
                
                CircleMemberPermissionSetGrantRequest = request.CircleMemberGrantRequest,
                AuthorizedCircles = request.AuthorizedCircles
            };

            _appRegistrationValueStorage.Upsert(appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            foreach (var circleId in appReg?.AuthorizedCircles ?? new List<Guid>())
            {
                //get all circle members and update their grants
                var members = await _circleNetworkService.GetCircleMembers(circleId);

                foreach (var member in members)
                {
                    var icr = await _circleNetworkService.GetIdentityConnectionRegistration(member);
                    var key = appReg.AppId.ToBase64();
                    var keyStoreKey = icr.AccessGrant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
                    
                    var circleMemberGrant = await _exchangeGrantService.CreateExchangeGrant(request.CircleMemberGrantRequest.PermissionSet, request.Drives, masterKey);
                    var cg = new CircleGrant()
                    {
                        CircleId = circleId,
                        KeyStoreKeyEncryptedDriveGrants = circleMemberGrant.KeyStoreKeyEncryptedDriveGrants,
                        PermissionSet = circleMemberGrant.PermissionSet,
                    };
                    
                    icr.AccessGrant.CircleGrants[key] = cg;
                    keyStoreKey.Wipe();
                }
            }

            return appReg.Redacted();
        }

        public async Task UpdateAppPermissions(UpdateAppPermissionsRequest request)
        {
            var appReg = await this.GetAppRegistrationInternal(request.AppId);
            if (null == appReg)
            {
                throw new YouverseClientException("Invalid AppId", YouverseClientErrorCode.AppNotRegistered);
            }

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var grant = await _exchangeGrantService.CreateExchangeGrant(request.PermissionSet, request.Drives, masterKey);
            appReg.Grant = grant;

            _appRegistrationValueStorage.Upsert(request.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        public async Task UpdateAuthorizedCircles(UpdateAuthorizedCirclesRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(request.AppId);
            if (null == appReg)
            {
                throw new YouverseClientException("Invalid AppId", YouverseClientErrorCode.AppNotRegistered);
            }

            //TODO: examine if the circles changed - update exchange grants
            // bool circlesHaveChanged = false;
            // if (circlesHaveChanged)
            // {
            //     //TODO: how to apply the permissions to all users with-in the circles
            // }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var circleMemberGrant = await _exchangeGrantService.CreateExchangeGrant(request.CircleMemberPermissionSet, request.CircleMemberDrives, masterKey);
            appReg.AuthorizedCircles = request.AuthorizedCircles;

            throw new NotImplementedException("");

            _appRegistrationValueStorage.Upsert(request.AppId, GuidId.Empty, _appRegistrationDataType, appReg);
            ResetPermissionContextCache();
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

            var appClient = new AppClient(appId, friendlyName, accessRegistration);
            this.SaveClient(appClient);

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

        private void SaveClient(AppClient appClient)
        {
            _appClientValueStorage.Upsert(appClient.AccessRegistration.Id, appClient.AppId, _appClientDataType, appClient);
        }

        public async Task<RedactedAppRegistration> GetAppRegistration(GuidId appId)
        {
            var result = await GetAppRegistrationInternal(appId);
            return result?.Redacted();
        }

        public async Task<DotYouContext> GetAppPermissionContext(ClientAuthenticationToken token)
        {
            var creator = new Func<Task<DotYouContext>>(async delegate
            {
                var (isValid, accessReg, appReg) = this.ValidateClientAuthToken(token).GetAwaiter().GetResult();

                if (!isValid)
                {
                    throw new YouverseSecurityException("Invalid token");
                }

                var grantDictionary = new Dictionary<string, ExchangeGrant>
                {
                    { "app_exchange_grant", appReg.Grant }
                };

                //Note: isOwner = true because we passed ValidateClientAuthToken for an ap token above 
                var permissionContext = _exchangeGrantService.CreatePermissionContext(token,
                    grantDictionary,
                    accessReg,
                    includeAnonymousDrives: true).GetAwaiter().GetResult();

                var dotYouContext = new DotYouContext()
                {
                    Caller = new CallerContext(
                        dotYouId: _tenantContext.HostDotYouId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Owner)
                };

                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            });

            var result = await _cache.GetOrAddContext(token, creator);
            return result;
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

            ResetPermissionContextCache();
        }

        /// <summary>
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        private void ResetPermissionContextCache()
        {
            _cache.Reset();
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

            ResetPermissionContextCache();
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients()
        {
            var list = _appClientValueStorage.GetByKey3<AppClient>(_appClientDataType);
            var resp = list.Select(appClient => new RegisteredAppClientResponse()
            {
                AppId = appClient.AppId,
                AccessRegistrationId = appClient.AccessRegistration.Id,
                FriendlyName = appClient.FriendlyName,
                IsRevoked = appClient.AccessRegistration.IsRevoked,
                Created = appClient.AccessRegistration.Created,
                AccessRegistrationClientType = appClient.AccessRegistration.AccessRegistrationClientType
            }).ToList();

            return resp;
        }

        public async Task RevokeClient(GuidId accessRegistrationId)
        {
            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new YouverseClientException("Invalid access reg id", YouverseClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = true;
            SaveClient(client);
        }

        public async Task AllowClient(GuidId accessRegistrationId)
        {
            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new YouverseClientException("Invalid access reg id", YouverseClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = false;
            SaveClient(client);
        }

        public async Task DeleteApp(GuidId appId)
        {
            var app = await GetAppRegistrationInternal(appId);

            if (null == app)
            {
                throw new YouverseClientException("Invalid App Id", YouverseClientErrorCode.AppNotRegistered);
            }

            _appRegistrationValueStorage.Delete(appId);
        }

        public async Task DeleteClient(GuidId accessRegistrationId)
        {
            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new YouverseClientException("Invalid access reg id", YouverseClientErrorCode.InvalidAccessRegistrationId);
            }

            _appClientValueStorage.Delete(accessRegistrationId);
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
    }
}