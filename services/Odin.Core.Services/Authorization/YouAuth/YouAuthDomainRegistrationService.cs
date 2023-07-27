#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Odin.Core.Exceptions;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Storage;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth
{
    public class YouAuthDomainRegistrationService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;

        private readonly GuidId _appRegistrationDataType = GuidId.FromString("__youauth_domain_reg");
        private readonly ThreeKeyValueStorage _registrationValueStorage;

        private readonly GuidId _appClientDataType = GuidId.FromString("__youauth_domain_client_reg");
        private readonly ThreeKeyValueStorage _youAuthDomainClientValueStorage;

        private readonly OdinContextCache _cache;
        private readonly TenantContext _tenantContext;

        private readonly IMediator _mediator;

        public YouAuthDomainRegistrationService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext, IMediator mediator, IcrKeyService icrKeyService)
        {
            _contextAccessor = contextAccessor;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _mediator = mediator;
            _icrKeyService = icrKeyService;

            _registrationValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _youAuthDomainClientValueStorage = tenantSystemStorage.ThreeKeyValueStorage;
            _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        /// <summary>
        /// Registers the domain as having access 
        /// </summary>
        public async Task<RedactedYouAuthDomainRegistration> RegisterDomain(YouAuthDomainRegistrationRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            Guard.Argument(request.Name, nameof(request.Name)).NotNull().NotEmpty();
            Guard.Argument(request.Domain, nameof(request.Domain)).Require(!string.IsNullOrEmpty(request.Domain.DomainName));

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var icrKey = (request.PermissionSet?.HasKey(PermissionKeys.UseTransit) ?? false) ? _icrKeyService.GetDecryptedIcrKey() : null;
            var appGrant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, request.PermissionSet!, request.Drives, masterKey, icrKey);

            //TODO: add check to ensure app name is unique
            //TODO: add check if app is already registered

            var reg = new YouAuthDomainRegistration()
            {
                Domain = request.Domain,
                Name = request.Name,
                Grant = appGrant,

                CorsHostName = request.CorsHostName,
            };

            _registrationValueStorage.Upsert(GetDomainKey(reg.Domain), GuidId.Empty, _appRegistrationDataType, reg);

            await NotifyAppChanged(null, reg);
            return reg.Redacted();
        }

        /// <summary>
        /// Updates the permissions granted to the domain when new clients are created
        /// </summary>
        public async Task UpdatePermissions(UpdateYouAuthDomainPermissionsRequest request)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var appReg = await this.GetDomainRegistrationInternal(request.Domain);
            if (null == appReg)
            {
                throw new OdinClientException("Invalid AppId", OdinClientErrorCode.AppNotRegistered);
            }

            //TODO: Should we regen the key store key?  

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = appReg.Grant.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var icrKey = request.PermissionSet?.HasKey(PermissionKeys.UseTransit) ?? false ? _icrKeyService.GetDecryptedIcrKey() : null;
            appReg.Grant = await _exchangeGrantService.CreateExchangeGrant(keyStoreKey, request.PermissionSet!, request.Drives, masterKey, icrKey);

            _registrationValueStorage.Upsert(GetDomainKey(request.Domain), GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(AsciiDomainName domain, string friendlyName,
            YouAuthDomainRegistrationRequest request = null)
        {
            Guard.Argument(domain, nameof(domain)).Require(x => !string.IsNullOrEmpty(x.DomainName));
            Guard.Argument(friendlyName, nameof(friendlyName)).NotNull().NotEmpty();

            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var reg = await this.GetDomainRegistrationInternal(domain);
            if (reg == null)
            {
                if (request == null)
                {
                    throw new OdinClientException($"{domain} not registered");
                }
                
                await this.RegisterDomain(request);
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(reg!.Grant, masterKey, ClientTokenType.Other);

            var youAuthDomainClient = new YouAuthDomainClient(domain, friendlyName, accessRegistration);
            this.SaveClient(youAuthDomainClient);
            return (cat, reg.CorsHostName);
        }

        public async Task<RedactedYouAuthDomainRegistration?> GetRegistration(AsciiDomainName domain)
        {
            var result = await GetDomainRegistrationInternal(domain);
            return result?.Redacted();
        }

        /// <summary>
        /// Determines if the specified domain requires consent from the owner before ...
        /// </summary>
        public async Task<bool> IsConsentRequired(AsciiDomainName domain)
        {
            var reg = await this.GetDomainRegistrationInternal(domain);

            if (null == reg)
            {
                return true;
            }
            
            if (reg.DeviceRegistrationConsentRequirement == ConsentRequirement.Always)
            {
                return true;
            }

            if (reg.DeviceRegistrationConsentRequirement == ConsentRequirement.Never)
            {
                return false;
            }

            return true;
        }
        
        public async Task<OdinContext> GetAppPermissionContext(ClientAuthenticationToken token)
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
                        appContext: new OdinAppContext()
                        {
                            CorsAppName = appReg.CorsHostName,
                            AccessRegistrationId = accessReg.Id
                        })
                };

                dotYouContext.SetPermissionContext(permissionContext);
                return dotYouContext;
            }

            var result = await _cache.GetOrAddContext(token, Creator);
            return result;
        }

        public async Task<(bool isValid, AccessRegistration? accessReg, YouAuthDomainRegistration? youAuthDomainRegistration)> ValidateClientAuthToken(
            ClientAuthenticationToken authToken)
        {
            var appClient = _youAuthDomainClientValueStorage.Get<YouAuthDomainClient>(authToken.Id);
            if (null == appClient)
            {
                return (false, null, null)!;
            }

            var reg = await this.GetDomainRegistrationInternal(appClient.Domain);

            if (null == reg || null == reg.Grant)
            {
                return (false, null, null)!;
            }

            if (appClient.AccessRegistration.IsRevoked || reg.Grant.IsRevoked)
            {
                return (false, null, null)!;
            }

            return (true, appClient.AccessRegistration, reg);
        }

        public async Task RevokeDomain(AsciiDomainName domain)
        {
            var appReg = await this.GetDomainRegistrationInternal(domain);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = true;
            }

            //TODO: revoke all clients? or is the one flag enough?
            _registrationValueStorage.Upsert(GetDomainKey(domain), GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        public async Task RemoveDomainRevocation(AsciiDomainName domain)
        {
            var appReg = await this.GetDomainRegistrationInternal(domain);
            if (null != appReg)
            {
                //TODO: do we do anything with storage DEK here?
                appReg.Grant.IsRevoked = false;
            }

            _registrationValueStorage.Upsert(GetDomainKey(domain), GuidId.Empty, _appRegistrationDataType, appReg);

            ResetPermissionContextCache();
        }

        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients()
        {
            var list = _youAuthDomainClientValueStorage.GetByKey3<YouAuthDomainClient>(_appClientDataType);
            var resp = list.Select(appClient => new RedactedYouAuthDomainClient()
            {
                Domain = appClient.Domain,
                AccessRegistrationId = appClient.AccessRegistration.Id,
                FriendlyName = appClient.FriendlyName,
                IsRevoked = appClient.AccessRegistration.IsRevoked,
                Created = appClient.AccessRegistration.Created,
                AccessRegistrationClientType = appClient.AccessRegistration.AccessRegistrationClientType
            }).ToList();

            return await Task.FromResult(resp);
        }

        public async Task RevokeClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();
            var client = _youAuthDomainClientValueStorage.Get<YouAuthDomainClient>(accessRegistrationId);

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
            var accessRegistrationId = context.Caller.AppContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete app client");
            }

            var client = _youAuthDomainClientValueStorage.Get<YouAuthDomainClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _youAuthDomainClientValueStorage.Delete(accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task DeleteClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var client = _youAuthDomainClientValueStorage.Get<YouAuthDomainClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _youAuthDomainClientValueStorage.Delete(accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task AllowClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var client = _youAuthDomainClientValueStorage.Get<YouAuthDomainClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            client.AccessRegistration.IsRevoked = false;
            SaveClient(client);
            await Task.CompletedTask;
        }

        public async Task DeleteDomainRegistration(AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var app = await GetDomainRegistrationInternal(domain);

            if (null == app)
            {
                throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
            }

            _registrationValueStorage.Delete(GetDomainKey(domain));
            await Task.CompletedTask;
        }

        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains()
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var apps = _registrationValueStorage.GetByKey3<YouAuthDomainRegistration>(_appRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return await Task.FromResult(redactedList);
        }

        private void SaveClient(YouAuthDomainClient youAuthDomainClient)
        {
            _youAuthDomainClientValueStorage.Upsert(youAuthDomainClient.AccessRegistration.Id, GetDomainKey(youAuthDomainClient.Domain).ToByteArray(), _appClientDataType,
                youAuthDomainClient);
        }

        private async Task<YouAuthDomainRegistration?> GetDomainRegistrationInternal(AsciiDomainName domain)
        {
            var key = GuidId.FromString(domain.DomainName);
            var reg = _registrationValueStorage.Get<YouAuthDomainRegistration>(key);
            return await Task.FromResult(reg);
        }

        private async Task NotifyAppChanged(YouAuthDomainRegistration? oldAppRegistration, YouAuthDomainRegistration newAppRegistration)
        {
            // await _mediator.Publish(new AppRegistrationChangedNotification()
            // {
            //     OldAppRegistration = oldAppRegistration,
            //     NewAppRegistration = newAppRegistration
            // });
        }

        /// <summary>
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        private void ResetPermissionContextCache()
        {
            _cache.Reset();
        }

        private Guid GetDomainKey(AsciiDomainName domainName)
        {
            return GuidId.FromString(domainName.DomainName);
        }
    }
}