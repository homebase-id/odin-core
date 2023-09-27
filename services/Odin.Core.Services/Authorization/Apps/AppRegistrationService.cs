﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Storage;


namespace Odin.Core.Services.Authorization.Apps
{
    public class AppRegistrationService : IAppRegistrationService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;

        private readonly TenantSystemStorage _tenantSystemStorage;

        private readonly GuidId _appRegistrationDataType = GuidId.FromString("__app_reg");
        private readonly ThreeKeyValueStorage _appRegistrationValueStorage;

        private readonly GuidId _appClientDataType = GuidId.FromString("__app_client_reg");
        private readonly ThreeKeyValueStorage _appClientValueStorage;

        private readonly OdinContextCache _cache;
        private readonly TenantContext _tenantContext;

        private readonly IMediator _mediator;

        public AppRegistrationService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext, IMediator mediator, IcrKeyService icrKeyService)
        {
            _contextAccessor = contextAccessor;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _mediator = mediator;
            _icrKeyService = icrKeyService;

            _tenantSystemStorage = tenantSystemStorage;

            _appRegistrationValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _appClientValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        public async Task<RedactedAppRegistration> RegisterApp(AppRegistrationRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();
            Guard.Argument(request.AppId, nameof(request.AppId)).Require(request.AppId != Guid.Empty);

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var hasTransit = (request.PermissionSet?.HasKey(PermissionKeys.UseTransitRead) ?? false) ||
                             (request.PermissionSet?.HasKey(PermissionKeys.UseTransitWrite) ?? false);
            var icrKey = hasTransit ? _icrKeyService.GetDecryptedIcrKey() : null;
            var appGrant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, request.PermissionSet!, request.Drives, masterKey, icrKey);

            //TODO: add check to ensure app name is unique
            //TODO: add check if app is already registered

            var appReg = new AppRegistration()
            {
                AppId = request.AppId,
                Name = request.Name,
                Grant = appGrant,

                CorsHostName = request.CorsHostName,
                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles,
                ConsentRequirements = ConsentRequirements.Default
            };

            _appRegistrationValueStorage.Upsert(appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            await NotifyAppChanged(null, appReg);
            return appReg.Redacted();
        }

        public async Task UpdateAppPermissions(UpdateAppPermissionsRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(request.AppId);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            //TODO: Should we regen the key store key?  

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = appReg.Grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var icrKey = request.PermissionSet?.HasKey(PermissionKeys.UseTransitWrite) ?? false ? _icrKeyService.GetDecryptedIcrKey() : null;
            appReg.Grant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, request.PermissionSet!, request.Drives, masterKey, icrKey);

            _appRegistrationValueStorage.Upsert(request.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task UpdateAuthorizedCircles(UpdateAuthorizedCirclesRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var oldRegistration = await this.GetAppRegistrationInternal(request.AppId);
            if (null == oldRegistration)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            var updatedAppReg = new AppRegistration()
            {
                AppId = oldRegistration.AppId,
                Name = oldRegistration.Name,
                Grant = oldRegistration.Grant,

                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant,
                AuthorizedCircles = request.AuthorizedCircles
            };

            _appRegistrationValueStorage.Upsert(request.AppId, GuidId.Empty, _appRegistrationDataType, updatedAppReg);

            //TODO: consider optimize by checking if anything actually changed before calling notify app changed

            await NotifyAppChanged(oldRegistration, updatedAppReg);
            ResetAppPermissionContextCache();
        }

        public async Task<(AppClientRegistrationResponse registrationResponse, string corsHostName)> RegisterClientPk(GuidId appId, byte[] clientPublicKey,
            string friendlyName)
        {
            var (cat, corsHostName) = await this.RegisterClient(appId, friendlyName);

            var data = cat.ToPortableBytes();
            var publicKey = RsaPublicKeyData.FromDerEncodedPublicKey(clientPublicKey);
            var encryptedData = publicKey.Encrypt(data);

            data.WriteZeros();

            var response = new AppClientRegistrationResponse()
            {
                EncryptionVersion = 1,
                Token = cat.Id,
                Data = encryptedData
            };

            return (response, corsHostName);
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(GuidId appId, string friendlyName)
        {
            Guard.Argument(appId, nameof(appId)).Require(x => x != Guid.Empty);
            Guard.Argument(friendlyName, nameof(friendlyName)).NotNull().NotEmpty();

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (appReg == null)
            {
                throw new OdinClientException("App must be registered to add a client", OdinClientErrorCode.AppNotRegistered);
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(appReg.Grant, masterKey, ClientTokenType.Other);

            var appClient = new AppClient(appId, friendlyName, accessRegistration);
            this.SaveClient(appClient);
            return (cat, appReg.CorsHostName);
        }

        public async Task<RedactedAppRegistration?> GetAppRegistration(GuidId appId)
        {
            var result = await GetAppRegistrationInternal(appId);
            return result?.Redacted();
        }

        public async Task<OdinContext?> GetAppPermissionContext(ClientAuthenticationToken token)
        {
            async Task<OdinContext> Creator()
            {
                var (isValid, accessReg, appReg) = await ValidateClientAuthToken(token);

                if (!isValid || null == appReg || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                if (!string.IsNullOrEmpty(appReg.CorsHostName))
                {
                    //just in case something changed in the db record
                    AppUtil.AssertValidCorsHeader(appReg.CorsHostName);
                }

                var grantDictionary = new Dictionary<Guid, ExchangeGrant> { { ByteArrayUtil.ReduceSHA256Hash("app_exchange_grant"), appReg.Grant } };

                //Note: isOwner = true because we passed ValidateClientAuthToken for an ap token above 
                var permissionContext = await _exchangeGrantService.CreatePermissionContext(token, grantDictionary, accessReg, includeAnonymousDrives: true);

                var dotYouContext = new OdinContext()
                {
                    Caller = new CallerContext(
                        odinId: _tenantContext.HostOdinId,
                        masterKey: null,
                        securityLevel: SecurityGroupType.Owner,
                        youAuthClientContext: new OdinYouAuthClientContext()
                        {
                            CorsHostName = appReg.CorsHostName,
                            AccessRegistrationId = accessReg.Id
                        })
                };

                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            }

            var result = await _cache.GetOrAddContext(token, Creator);
            return result;
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, AppRegistration? appRegistration)> ValidateClientAuthToken(
            ClientAuthenticationToken authToken)
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

            ResetAppPermissionContextCache();
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

            ResetAppPermissionContextCache();
        }

        public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients(GuidId appId)
        {
            var list = _appClientValueStorage.GetByKey3<AppClient>(_appClientDataType);
            var resp = list.Where(appClient => appClient.AppId == appId).Select(appClient => new RegisteredAppClientResponse()
            {
                AppId = appClient.AppId,
                AccessRegistrationId = appClient.AccessRegistration.Id,
                FriendlyName = appClient.FriendlyName,
                IsRevoked = appClient.AccessRegistration.IsRevoked,
                Created = appClient.AccessRegistration.Created,
                AccessRegistrationClientType = appClient.AccessRegistration.AccessRegistrationClientType
            }).ToList();

            return await Task.FromResult(resp);
        }

        public async Task<bool> IsConsentRequired(Guid appId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            return appReg.ConsentRequirements.IsRequired();
        }

        public async Task RevokeClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = true;
            SaveClient(client);
            await Task.CompletedTask;
        }

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an app
        /// </summary>
        public async Task DeleteCurrentAppClient()
        {
            var context = _contextAccessor.GetCurrent();
            var accessRegistrationId = context.Caller.YouAuthClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete app client");
            }

            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _appClientValueStorage.Delete(accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task UpdateConsentRequirements(Guid appId, ConsentRequirements consentRequirements)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetAppRegistrationInternal(appId);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }
            
            consentRequirements.Validate();
            
            appReg.ConsentRequirements = consentRequirements;

            _appRegistrationValueStorage.Upsert(appReg.AppId, GuidId.Empty, _appRegistrationDataType, appReg);

            ResetAppPermissionContextCache();
        }

        public async Task DeleteClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _appClientValueStorage.Delete(accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task AllowClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var client = _appClientValueStorage.Get<AppClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = false;
            SaveClient(client);
            await Task.CompletedTask;
        }

        public async Task DeleteApp(GuidId appId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var app = await GetAppRegistrationInternal(appId);

            if (null == app)
            {
                throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
            }

            _appRegistrationValueStorage.Delete(appId);

            //TODO: reenable this after youauth domain work

            //
            // var clientsByApp = _appClientValueStorage.GetByKey2<AppClient>(appId);
            // using (_tenantSystemStorage.CreateCommitUnitOfWork())
            // {
            //     foreach (var c in clientsByApp)
            //     {
            //         _appClientValueStorage.Delete(c.AccessRegistration.Id);
            //     }
            //
            //     _appRegistrationValueStorage.Delete(appId);
            // }

            await Task.CompletedTask;
        }

        public async Task<List<RedactedAppRegistration>> GetRegisteredApps()
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var apps = _appRegistrationValueStorage.GetByKey3<AppRegistration>(_appRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return await Task.FromResult(redactedList);
        }

        private void SaveClient(AppClient appClient)
        {
            _appClientValueStorage.Upsert(appClient.AccessRegistration.Id, appClient.AppId, _appClientDataType, appClient);
        }

        private async Task<AppRegistration?> GetAppRegistrationInternal(GuidId appId)
        {
            var appReg = _appRegistrationValueStorage.Get<AppRegistration>(appId);
            return await Task.FromResult(appReg);
        }

        private async Task NotifyAppChanged(AppRegistration? oldAppRegistration, AppRegistration newAppRegistration)
        {
            await _mediator.Publish(new AppRegistrationChangedNotification()
            {
                OldAppRegistration = oldAppRegistration,
                NewAppRegistration = newAppRegistration
            });
        }

        /// <summary>
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        private void ResetAppPermissionContextCache()
        {
            _cache.Reset();
        }
    }
}