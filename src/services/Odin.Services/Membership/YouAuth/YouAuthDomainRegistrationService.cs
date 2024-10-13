#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Services.Membership.YouAuth
{
    public class YouAuthDomainRegistrationService
    {
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

        public YouAuthDomainRegistrationService(TenantSystemStorage tenantSystemStorage,
            ExchangeGrantService exchangeGrantService, OdinConfiguration config, TenantContext tenantContext,
            CircleNetworkService circleNetworkService, CircleMembershipService circleMembershipService)
        {
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
        public async Task<RedactedYouAuthDomainRegistration> RegisterDomain(YouAuthDomainRegistrationRequest request, IOdinContext odinContext,
            IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();


            OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
            OdinValidationUtils.AssertNotNullOrEmpty(request.Domain, nameof(request.Domain));

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            if (null != await this.GetDomainRegistrationInternal(new AsciiDomainName(request.Domain)))
            {
                throw new OdinClientException("Domain already registered");
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var grants = await _circleMembershipService.CreateCircleGrantList(keyStoreKey, request.CircleIds ?? new List<GuidId>(), masterKey, odinContext, db);

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

            this.SaveRegistration(reg, odinContext, db);

            return reg.Redacted();
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClient(
            AsciiDomainName domain,
            string friendlyName,
            YouAuthDomainRegistrationRequest? request,
            IOdinContext odinContext,
            IdentityDatabase db)
        {
            OdinValidationUtils.AssertNotNullOrEmpty(friendlyName, nameof(friendlyName));
            odinContext.Caller.AssertHasMasterKey();

            var reg = await this.GetDomainRegistrationInternal(domain);
            if (reg == null)
            {
                if (request == null)
                {
                    throw new OdinClientException($"{domain} not registered");
                }

                await this.RegisterDomain(request, odinContext, db);
                reg = await this.GetDomainRegistrationInternal(domain);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = reg!.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var (accessRegistration, cat) = await _exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.YouAuth);

            var youAuthDomainClient = new YouAuthDomainClient(domain, friendlyName, accessRegistration);
            this.SaveClient(youAuthDomainClient, db);
            return (cat, reg.CorsHostName);
        }

        public async Task<RedactedYouAuthDomainRegistration?> GetRegistration(AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var result = await GetDomainRegistrationInternal(domain);
            return result?.Redacted();
        }

        /// <summary>
        /// Determines if the specified domain requires consent from the owner before ...
        /// </summary>
        public async Task<bool> IsConsentRequired(AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (await _circleNetworkService.IsConnected((OdinId)domain.DomainName, odinContext, db))
            {
                return false;
            }

            var reg = await this.GetDomainRegistrationInternal(domain);

            return reg?.ConsentRequirements?.IsRequired() ?? true;
        }

        public async Task UpdateConsentRequirements(AsciiDomainName domain, ConsentRequirements consentRequirements, IOdinContext odinContext,
            IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            consentRequirements.Validate();

            var domainReg = await this.GetDomainRegistrationInternal(domain);
            if (null == domainReg)
            {
                throw new OdinClientException("Domain not registered");
            }

            domainReg.ConsentRequirements = consentRequirements;

            this.SaveRegistration(domainReg, odinContext, db);
            ResetPermissionContextCache();
        }

        public async Task RevokeDomain(AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var domainReg = await this.GetDomainRegistrationInternal(domain);
            if (null == domainReg)
            {
                return;
            }

            //TODO: do we do anything with storage DEK here?
            domainReg.IsRevoked = true;
            this.SaveRegistration(domainReg, odinContext, db);
            ResetPermissionContextCache();
        }

        public async Task RemoveDomainRevocation(AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var domainReg = await this.GetDomainRegistrationInternal(domain);
            if (null == domainReg)
            {
                return;
            }

            //TODO: do we do anything with storage DEK here?
            domainReg.IsRevoked = false;
            this.SaveRegistration(domainReg, odinContext, db);
            ResetPermissionContextCache();
        }

        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClients(AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var list = _clientStorage.GetByCategory<YouAuthDomainClient>(db, _clientDataType);
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
        public async Task DeleteCurrentYouAuthDomainClient(IOdinContext odinContext, IdentityDatabase db)
        {
            var context = odinContext;
            var accessRegistrationId = context.Caller.OdinClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete domain client");
            }

            var client = _clientStorage.Get<YouAuthDomainClient>(db, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _clientStorage.Delete(db, accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task DeleteClient(GuidId accessRegistrationId, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var client = _clientStorage.Get<YouAuthDomainClient>(db, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            _clientStorage.Delete(db, accessRegistrationId);
            await Task.CompletedTask;
        }

        public async Task DeleteDomainRegistration(AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var reg = await GetDomainRegistrationInternal(domain);

            if (null == reg)
            {
                throw new OdinClientException("Invalid domain", OdinClientErrorCode.DomainNotRegistered);
            }

            //delete the clients
            var clientsByDomain = _clientStorage.GetByDataType<YouAuthDomainClient>(db, GetDomainKey(domain).ToByteArray());

            // TODO CONNECTIONS
            // db.CreateCommitUnitOfWork(() => {
            foreach (var c in clientsByDomain)
            {
                _clientStorage.Delete(db, c.AccessRegistration.Id);
            }

            _domainStorage.Delete(db, GetDomainKey(domain));
            // });

            await Task.CompletedTask;
        }

        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomains(IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

            var domains = _domainStorage.GetByCategory<YouAuthDomainRegistration>(db, _domainRegistrationDataType);
            var redactedList = domains.Select(d => d.Redacted()).ToList();
            return await Task.FromResult(redactedList);
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the YouAuthDomain
        /// </summary>
        public async Task GrantCircle(GuidId circleId, AsciiDomainName domainName, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

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

            var circleDefinition = _circleMembershipService.GetCircle(circleId, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = registration.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var circleGrant = await _circleMembershipService.CreateCircleGrant(keyStoreKey, circleDefinition, masterKey, odinContext, db);

            registration.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            keyStoreKey.Wipe();

            this.SaveRegistration(registration, odinContext, db);

            ResetPermissionContextCache();
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccess(GuidId circleId, AsciiDomainName domain, IOdinContext odinContext, IdentityDatabase db)
        {
            odinContext.Caller.AssertHasMasterKey();

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

            this.SaveRegistration(registration, odinContext, db);

            ResetPermissionContextCache();
        }

        // 

        public async Task<IOdinContext?> GetDotYouContext(ClientAuthenticationToken token, IOdinContext currentOdinContext, IdentityDatabase db)
        {
            async Task<IOdinContext> Creator()
            {
                var (isValid, accessReg, domainRegistration) = await ValidateClientAuthToken(token, db);

                if (!isValid || null == domainRegistration || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                //
                // If the domain is from an odin identity that is connected, upgrade their permissions
                //
                var odinId = (OdinId)domainRegistration.Domain.DomainName;
                var odinContext = await _circleNetworkService.TryCreateConnectedYouAuthContext(odinId, token, accessReg, currentOdinContext, db);
                if (null != odinContext)
                {
                    return odinContext;
                }

                return await CreateAuthenticatedContextForYouAuthDomain(token, domainRegistration, accessReg, currentOdinContext);
            }

            var result = await _cache.GetOrAddContext(token, Creator);
            return result;
        }

        private async Task<(bool isValid, AccessRegistration? accessReg, YouAuthDomainRegistration? youAuthDomainRegistration)> ValidateClientAuthToken(
            ClientAuthenticationToken authToken, IdentityDatabase db)
        {
            var domainClient = _clientStorage.Get<YouAuthDomainClient>(db, authToken.Id);
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

        private void SaveClient(YouAuthDomainClient youAuthDomainClient, IdentityDatabase db)
        {
            _clientStorage.Upsert(db, youAuthDomainClient.AccessRegistration.Id, GetDomainKey(youAuthDomainClient.Domain).ToByteArray(),
                _clientDataType,
                youAuthDomainClient);
        }

        private async Task<YouAuthDomainRegistration?> GetDomainRegistrationInternal(AsciiDomainName domain)
        {
            var key = GuidId.FromString(domain.DomainName);
            var reg = _domainStorage.Get<YouAuthDomainRegistration>(_tenantSystemStorage.IdentityDatabase, key);

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

        private void SaveRegistration(YouAuthDomainRegistration registration, IOdinContext odinContext, IdentityDatabase db)
        {
            var domain = new OdinId(registration.Domain);

            // TODO CONNECTIONS
            // db.CreateCommitUnitOfWork(() => {
            //Store the circles for this registration

            //TODO: this is causing an issue where in the circles are also deleted for the ICR 
            // 
            _circleMembershipService.DeleteMemberFromAllCircles(registration.Domain, DomainType.YouAuth);

            foreach (var (circleId, circleGrant) in registration.CircleGrants)
            {
                var circleMembers = _circleMembershipService.GetDomainsInCircle(circleId, odinContext)
                    .Where(d => d.DomainType == DomainType.YouAuth);
                var isMember = circleMembers.Any(d =>
                    OdinId.ToHashId(d.Domain) == OdinId.ToHashId(registration.Domain));

                if (!isMember)
                {
                    //
                    // BUG - this method calls upsert which will overwrite a circle membership
                    // from the DomainType.Identity when it is granted to the DomainType.youauth
                    //
                    _circleMembershipService.AddCircleMember(circleId, domain, circleGrant, DomainType.YouAuth);
                }
            }

            //clear them here so we don't have two locations
            registration.CircleGrants.Clear();

            _domainStorage.Upsert(db, GetDomainKey(registration.Domain), GuidId.Empty, _domainRegistrationDataType,
                registration);
            // });
        }

        private async Task<IOdinContext> CreateAuthenticatedContextForYouAuthDomain(
            ClientAuthenticationToken authToken,
            YouAuthDomainRegistration domainRegistration,
            AccessRegistration accessReg,
            IOdinContext odinContext)
        {
            if (!string.IsNullOrEmpty(domainRegistration.CorsHostName))
            {
                //just in case something changed in the db record
                AppUtil.AssertValidCorsHeader(domainRegistration.CorsHostName);
            }

            //TODO: do we want allow youauthdomains to have these permissions?

            var permissionKeys = _tenantContext.Settings.GetAdditionalPermissionKeysForAuthenticatedIdentities();
            var anonymousDrivePermissions = _tenantContext.Settings.GetAnonymousDrivePermissionsForAuthenticatedIdentities();

            var (grants, enabledCircles) =
                _circleMembershipService.MapCircleGrantsToExchangeGrants(domainRegistration.Domain, domainRegistration.CircleGrants.Values.ToList(),
                    odinContext);

            var permissionContext = await _exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                odinContext: odinContext,
                db: _tenantSystemStorage.IdentityDatabase,
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
                        AccessRegistrationId = accessReg.Id,
                        DevicePushNotificationKey = null
                    })
            };

            dotYouContext.SetPermissionContext(permissionContext);
            return dotYouContext;
        }
    }
}