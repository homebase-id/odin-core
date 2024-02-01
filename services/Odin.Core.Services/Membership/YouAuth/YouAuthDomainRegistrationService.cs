﻿#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Membership.CircleMembership;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;

namespace Odin.Core.Services.Membership.YouAuth
{
    public class YouAuthDomainRegistrationService
    {
        private readonly OdinContextAccessor _contextAccessor;
        private readonly ExchangeGrantService _exchangeGrantService;
        private readonly CircleNetworkService _circleNetworkService;
        private readonly CircleMembershipService _circleMembershipService;
        private readonly TenantSystemStorage _tenantSystemStorage;

        private readonly byte[] _domainRegistrationDataType = Guid.Parse("0c2c70c2-86e9-4214-818d-8b57c8d59762").ToByteArray();
        private readonly ThreeKeyValueStorage _domainStorage;

        private readonly byte[] _clientDataType = Guid.Parse("cd16bc37-3e1f-410b-be03-7bec83dd6c33").ToByteArray();
        private readonly ThreeKeyValueStorage _clientStorage;

        private readonly OdinContextCache _cache;
        private readonly TenantContext _tenantContext;

        public YouAuthDomainRegistrationService(OdinContextAccessor contextAccessor, TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext,
            CircleNetworkService circleNetworkService, CircleMembershipService circleMembershipService)
        {
            _contextAccessor = contextAccessor;
            _tenantSystemStorage = tenantSystemStorage;
            _exchangeGrantService = exchangeGrantService;
            _tenantContext = tenantContext;
            _circleNetworkService = circleNetworkService;
            _circleMembershipService = circleMembershipService;

            const string domainStorageContextKey = "e11ff091-0edf-4532-8b0f-b9d9ebe0880f";
            _domainStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(domainStorageContextKey));

            const string domainClientStorageContextKey = "8994c20a-179c-469c-a3b9-c4d6a8d2eb3c";
            _clientStorage = tenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(domainClientStorageContextKey));

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

            request.ConsentRequirements?.Validate();

            var reg = new YouAuthDomainRegistration()
            {
                Domain = new AsciiDomainName(request.Domain),
                Created = UnixTimeUtc.Now().milliseconds,
                Modified = UnixTimeUtc.Now().milliseconds,
                Name = request.Name,
                CorsHostName = request.CorsHostName,
                MasterKeyEncryptedKeyStoreKey = new SymmetricKeyEncryptedAes(masterKey, keyStoreKey),
                CircleGrants = grants,
                ConsentRequirements = request.ConsentRequirements
            };

            this.SaveRegistration(reg);

            return reg.Redacted();
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(
            AsciiDomainName domain,
            string friendlyName,
            YouAuthDomainRegistrationRequest? request)
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
            var keyStoreKey = reg!.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.YouAuth);

            var youAuthDomainClient = new YouAuthDomainClient(domain, friendlyName, accessRegistration);
            this.SaveClient(youAuthDomainClient);
            return (cat, reg.CorsHostName);
        }

        public async Task<RedactedYouAuthDomainRegistration?> GetRegistration(AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var result = await GetDomainRegistrationInternal(domain);
            return result?.Redacted();
        }

        /// <summary>
        /// Determines if the specified domain requires consent from the owner before ...
        /// </summary>
        public async Task<bool> IsConsentRequired(AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            if (await _circleNetworkService.IsConnected((OdinId)domain.DomainName))
            {
                return false;
            }

            var reg = await this.GetDomainRegistrationInternal(domain);

            return reg?.ConsentRequirements?.IsRequired() ?? true;
        }

        public async Task UpdateConsentRequirements(AsciiDomainName domain, ConsentRequirements consentRequirements)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            consentRequirements.Validate();

            var domainReg = await this.GetDomainRegistrationInternal(domain);
            if (null == domainReg)
            {
                throw new OdinClientException("Domain not registered");
            }

            domainReg.ConsentRequirements = consentRequirements;

            this.SaveRegistration(domainReg);
            ResetPermissionContextCache();
        }

        public async Task RevokeDomain(AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

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
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

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

        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients(AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var list = _clientStorage.GetByCategory<YouAuthDomainClient>(_clientDataType);
            var resp = list.Where(d => d.Domain.DomainName.ToLower() == domain.DomainName.ToLower()).Select(domainClient => new RedactedYouAuthDomainClient()
            {
                Domain = domainClient.Domain,
                AccessRegistrationId = domainClient.AccessRegistration.Id,
                FriendlyName = domainClient.FriendlyName,
                IsRevoked = domainClient.AccessRegistration.IsRevoked,
                Created = domainClient.AccessRegistration.Created,
                AccessRegistrationClientType = domainClient.AccessRegistration.AccessRegistrationClientType
            }).ToList();

            return await Task.FromResult(resp);
        }

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an domain
        /// </summary>
        public async Task DeleteCurrentYouAuthDomainClient()
        {
            var context = _contextAccessor.GetCurrent();
            var accessRegistrationId = context.Caller.OdinClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete domain client");
            }

            var client = _clientStorage.Get<YouAuthDomainClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _clientStorage.Delete(accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task DeleteClient(GuidId accessRegistrationId)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var client = _clientStorage.Get<YouAuthDomainClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _clientStorage.Delete(accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task DeleteDomainRegistration(AsciiDomainName domain)
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var reg = await GetDomainRegistrationInternal(domain);

            if (null == reg)
            {
                throw new OdinClientException("Invalid domain", OdinClientErrorCode.DomainNotRegistered);
            }

            //delete the clients
            var clientsByDomain = _clientStorage.GetByDataType<YouAuthDomainClient>(GetDomainKey(domain).ToByteArray());

            using (_tenantSystemStorage.CreateCommitUnitOfWork())
            {
                foreach (var c in clientsByDomain)
                {
                    _clientStorage.Delete(c.AccessRegistration.Id);
                }

                _domainStorage.Delete(GetDomainKey(domain));
            }

            await Task.CompletedTask;
        }

        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains()
        {
            _contextAccessor.GetCurrent().Caller.AssertHasMasterKey();

            var domains = _domainStorage.GetByCategory<YouAuthDomainRegistration>(_domainRegistrationDataType);
            var redactedList = domains.Select(d => d.Redacted()).ToList();
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

            if (registration.CircleGrants.TryGetValue(circleId, out _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{domainName} is already member of circle", OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = _circleMembershipService.GetCircle(circleId);
            var masterKey = _contextAccessor.GetCurrent().Caller.GetMasterKey();
            var keyStoreKey = registration.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
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

        // 

        public async Task<OdinContext?> GetDotYouContext(ClientAuthenticationToken token)
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

                return await CreateAuthenticatedContextForYouAuthDomain(token, domainRegistration, accessReg);
            }

            var result = await _cache.GetOrAddContext(token, Creator);
            return result;
        }

        private async Task<(bool isValid, AccessRegistration? accessReg, YouAuthDomainRegistration? youAuthDomainRegistration)> ValidateClientAuthToken(
            ClientAuthenticationToken authToken)
        {
            var domainClient = _clientStorage.Get<YouAuthDomainClient>(authToken.Id);
            if (null == domainClient)
            {
                return (false, null, null);
            }

            var reg = await this.GetDomainRegistrationInternal(domainClient.Domain);

            if (null == reg)
            {
                return (false, null, null);
            }

            if (domainClient.AccessRegistration.IsRevoked || reg.IsRevoked)
            {
                return (false, null, null);
            }

            return (true, domainClient.AccessRegistration, reg);
        }

        // 

        private void SaveClient(YouAuthDomainClient youAuthDomainClient)
        {
            _clientStorage.Upsert(youAuthDomainClient.AccessRegistration.Id, GetDomainKey(youAuthDomainClient.Domain).ToByteArray(),
                _clientDataType,
                youAuthDomainClient);
        }

        private async Task<YouAuthDomainRegistration?> GetDomainRegistrationInternal(AsciiDomainName domain)
        {
            var key = GuidId.FromString(domain.DomainName);
            var reg = _domainStorage.Get<YouAuthDomainRegistration>(key);

            if (null != reg)
            {
                //get the circle grants for this domain
                var circles = _circleMembershipService.GetCirclesGrantsByDomain(reg.Domain, DomainType.YouAuth);
                reg.CircleGrants = circles.ToDictionary(cg => cg.CircleId.Value, cg => cg);
            }

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

            using (_tenantSystemStorage.CreateCommitUnitOfWork())
            {
                //Store the circles for this registration

                //TODO: this is causing an issue where in the circles are also deleted for the ICR 
                // 
                _circleMembershipService.DeleteMemberFromAllCircles(registration.Domain, DomainType.YouAuth);

                foreach (var (circleId, circleGrant) in registration.CircleGrants)
                {
                    var circleMembers = _circleMembershipService.GetDomainsInCircle(circleId).Where(d => d.DomainType == DomainType.YouAuth);
                    var isMember = circleMembers.Any(d => OdinId.ToHashId(d.Domain) == OdinId.ToHashId(registration.Domain));

                    if (!isMember)
                    {
                        _circleMembershipService.AddCircleMember(circleId, domain, circleGrant, DomainType.YouAuth);
                    }
                }

                //clear them here so we don't have two locations
                registration.CircleGrants.Clear();

                _domainStorage.Upsert(GetDomainKey(registration.Domain), GuidId.Empty, _domainRegistrationDataType, registration);
            }
        }

        private async Task<OdinContext> CreateAuthenticatedContextForYouAuthDomain(
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

            var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForAuthenticatedIdentities();
            var anonymousDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForAuthenticatedIdentities();

            var (grants, enabledCircles) = _circleMembershipService.MapCircleGrantsToExchangeGrants(domainRegistration.CircleGrants.Values.ToList());

            var permissionContext = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                additionalPermissionKeys: permissionKeys,
                includeAnonymousDrives: true,
                anonymousDrivePermission: anonymousDrivePermissions);

            var dotYouContext = new OdinContext()
            {
                Caller = new CallerContext(
                    odinId: new OdinId(domainRegistration.Domain.DomainName),
                    masterKey: null,
                    securityLevel: SecurityGroupType.Authenticated,
                    circleIds: enabledCircles,
                    odinClientContext: new OdinClientContext()
                    {
                        ClientIdOrDomain = domainRegistration.Domain.DomainName,
                        CorsHostName = domainRegistration.CorsHostName,
                        AccessRegistrationId = accessReg.Id
                    })
            };

            dotYouContext.SetPermissionContext(permissionContext);
            return dotYouContext;
        }
    }
}