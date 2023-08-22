#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.CircleMembership;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Services.Authorization.YouAuth
{
    public class YouAuthDomainRegistrationService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly IcrKeyService _icrKeyService;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly CircleMembershipService _circleMembershipService;

        private readonly GuidId _domainRegistrationDataType = GuidId.FromString("__youauth_domain_reg");
        private readonly ThreeKeyValueStorage _registrationValueStorage;

        private readonly GuidId _clientDataType = GuidId.FromString("__youauth_domain_client_reg");
        private readonly ThreeKeyValueStorage _youAuthDomainClientValueStorage;

        private readonly OdinContextCache _cache;
        private readonly TenantContext _tenantContext;

        private readonly IMediator _mediator;

        public YouAuthDomainRegistrationService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext, IMediator mediator, IcrKeyService icrKeyService,
            CircleNetworkService circleNetworkService, CircleMembershipService circleMembershipService)
        {
            _contextAccessor = contextAccessor;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _mediator = mediator;
            _icrKeyService = icrKeyService;
            _circleNetworkService = circleNetworkService;
            _circleMembershipService = circleMembershipService;

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
            Guard.Argument(request.Domain, nameof(request.Domain)).Require(!string.IsNullOrEmpty(request.Domain));

            // AssertValidPermissionSet(request.PermissionSet);

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            if (null != await this.GetDomainRegistrationInternal(new AsciiDomainName(request.Domain)))
            {
                throw new OdinClientException("Domain already registered");
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var grants = await _circleMembershipService.CreateCircleGrantList(request.CircleIds ?? new List<GuidId>(), keyStoreKey);

            var reg = new YouAuthDomainRegistration()
            {
                Domain = new AsciiDomainName(request.Domain),
                Created = UnixTimeUtc.Now().milliseconds,
                Modified = UnixTimeUtc.Now().milliseconds,
                Name = request.Name,
                CorsHostName = request.CorsHostName,
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(ref masterKey, ref keyStoreKey),
                CircleGrants = grants
            };

            this.SaveRegistration(reg);

            return reg.Redacted();
        }
        

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(
            AsciiDomainName domain,
            string friendlyName,
            YouAuthDomainRegistrationRequest request)
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
                reg = await this.GetDomainRegistrationInternal(domain);
            }

            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = reg!.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.TempUnknown);

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
            if (await _circleNetworkService.IsConnected((OdinId)domain.DomainName))
            {
                return false;
            }

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

        public async Task<OdinContext> GetDotYouContext(ClientAuthenticationToken token)
        {
            async Task<OdinContext> Creator()
            {
                var (isValid, accessReg, domainRegistration) = await ValidateClientAuthToken(token);

                if (!isValid || null == domainRegistration || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                //
                // If the domain is from an odin identity that is connected, upgrade their permissions
                //
                var odinId = (OdinId)domainRegistration.Domain.DomainName;
                var odinContext = await _circleNetworkService.TryCreateConnectedYouAuthContext(odinId, token, accessReg);
                if (null != odinContext)
                {
                    return odinContext;
                }

                return await CreateContextForYouAuthDomain(token, domainRegistration, accessReg);
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
                return (false, null, null);
            }

            var reg = await this.GetDomainRegistrationInternal(appClient.Domain);

            if (null == reg)
            {
                return (false, null, null);
            }

            if (appClient.AccessRegistration.IsRevoked || reg.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, appClient.AccessRegistration, reg);
        }

        public async Task RevokeDomain(AsciiDomainName domain)
        {
            var domainReg = await this.GetDomainRegistrationInternal(domain);
            if (null == domainReg)
            {
                return;
            }

            //TODO: do we do anything with storage DEK here?
            domainReg.IsRevoked = true;
            this.SaveRegistration(domainReg);
            ResetPermissionContextCache();
        }

        public async Task RemoveDomainRevocation(AsciiDomainName domain)
        {
            var domainReg = await this.GetDomainRegistrationInternal(domain);
            if (null == domainReg)
            {
                return;
            }

            //TODO: do we do anything with storage DEK here?
            domainReg.IsRevoked = false;
            this.SaveRegistration(domainReg);
            ResetPermissionContextCache();
        }

        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients()
        {
            var list = _youAuthDomainClientValueStorage.GetByKey3<YouAuthDomainClient>(_clientDataType);
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
        public async Task DeleteCurrentYouAuthDomainClient()
        {
            var context = _contextAccessor.GetCurrent();
            var accessRegistrationId = context.Caller.YouAuthContext?.AccessRegistrationId;

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

            var apps = _registrationValueStorage.GetByKey3<YouAuthDomainRegistration>(_domainRegistrationDataType);
            var redactedList = apps.Select(app => app.Redacted()).ToList();
            return await Task.FromResult(redactedList);
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the YouAuthDomain
        /// </summary>
        public async Task GrantCircle(GuidId circleId, AsciiDomainName domainName)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var registration = await this.GetDomainRegistrationInternal(domainName);

            if (registration == null)
            {
                throw new OdinSecurityException($"{domainName} is not registered");
            }

            if (registration.CircleGrants.TryGetValue(circleId, out var _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{domainName} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = _circleMembershipService.GetCircle(circleId);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = registration.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(ref masterKey);
            var circleGrant = await _circleMembershipService.CreateCircleGrant(circleDefinition, keyStoreKey, masterKey);

            registration.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            keyStoreKey.Wipe();
            
            this.SaveRegistration(registration);
            
            ResetPermissionContextCache();
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccess(GuidId circleId, AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var registration = await this.GetDomainRegistrationInternal(domain);
            if (registration == null)
            {
                return;
            }

            if (registration.CircleGrants.ContainsKey(circleId))
            {
                if (!registration.CircleGrants.Remove(circleId))
                {
                    throw new OdinClientException($"Failed to remove {circleId} from {domain}");
                }
            }

            this.SaveRegistration(registration);
            
            ResetPermissionContextCache();
        }
        
        private void SaveClient(YouAuthDomainClient youAuthDomainClient)
        {
            _youAuthDomainClientValueStorage.Upsert(youAuthDomainClient.AccessRegistration.Id, GetDomainKey(youAuthDomainClient.Domain).ToByteArray(),
                _clientDataType,
                youAuthDomainClient);
        }

        private async Task<YouAuthDomainRegistration?> GetDomainRegistrationInternal(AsciiDomainName domain)
        {
            var key = GuidId.FromString(domain.DomainName);
            var reg = _registrationValueStorage.Get<YouAuthDomainRegistration>(key);
            return await Task.FromResult(reg);
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

        private void SaveRegistration(YouAuthDomainRegistration registration)
        {
            var domain = new OdinId(registration.Domain);

            //Reconcile circle grants in the table
            _circleMembershipService.DeleteMemberFromAllCircles(domain);
            foreach (var (circleId, circleGrant) in registration.CircleGrants)
            {
                var circleMembers = _circleMembershipService.GetCircleMembers(circleId);
                var isMember = circleMembers.Any(odinId => odinId == domain);

                if (!isMember)
                {
                    _circleMembershipService.AddCircleMember(circleId, domain, circleGrant);
                }
            }

            _registrationValueStorage.Upsert(GetDomainKey(registration.Domain), GuidId.Empty, _domainRegistrationDataType, registration);
        }
        
        private void AssertValidPermissionSet(PermissionSet permissionSet)
        {
            if (permissionSet.Keys?.Any(k => !PermissionKeyAllowance.IsValidYouAuthDomainPermission(k)) ?? false)
            {
                throw new OdinClientException("Invalid Permission key specified");
            }
        }

        private async Task<OdinContext> CreateContextForYouAuthDomain(
            ClientAuthenticationToken authToken,
            YouAuthDomainRegistration domainRegistration,
            AccessRegistration accessReg)
        {
            if (!string.IsNullOrEmpty(domainRegistration.CorsHostName))
            {
                //just in case something changed in the db record
                AppUtil.AssertValidCorsHeader(domainRegistration.CorsHostName);
            }

            //TODO: do we want allow youauthdomains to have these permissions?

            List<int> permissionKeys = new List<int>();
            if (_tenantContext.Settings.AuthenticatedIdentitiesCanViewConnections)
            {
                permissionKeys.Add(PermissionKeys.ReadConnections);
            }

            if (_tenantContext.Settings.AuthenticatedIdentitiesCanViewWhoIFollow)
            {
                permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
            }

            var (grants, enabledCircles) = _circleMembershipService.MapCircleGrantsToExchangeGrants(domainRegistration.CircleGrants.Values.ToList());

            var permissionContext = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true);

            var dotYouContext = new OdinContext()
            {
                Caller = new CallerContext(
                    odinId: new OdinId(domainRegistration.Domain.DomainName),
                    masterKey: null,
                    securityLevel: SecurityGroupType.Authenticated,
                    circleIds: enabledCircles,
                    youAuthContext: new OdinYouAuthContext()
                    {
                        CorsHostName = domainRegistration.CorsHostName,
                        AccessRegistrationId = accessReg.Id
                    })
            };

            dotYouContext.SetPermissionContext(permissionContext);
            return dotYouContext;
        }
    }
}