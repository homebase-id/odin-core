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
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Services.Membership.YouAuth
{
    public class YouAuthDomainRegistrationService(
        ExchangeGrantService exchangeGrantService,
        TenantContext tenantContext,
        CircleNetworkService circleNetworkService,
        CircleMembershipService circleMembershipService,
        IdentityDatabase db,
        OdinContextCache cache)
    {
        private static readonly byte[] DomainRegistrationDataType = Guid.Parse("0c2c70c2-86e9-4214-818d-8b57c8d59762").ToByteArray();
        private const string DomainStorageContextKey = "e11ff091-0edf-4532-8b0f-b9d9ebe0880f";

        private static readonly ThreeKeyValueStorage DomainStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(DomainStorageContextKey));

        private static readonly byte[] ClientDataType = Guid.Parse("cd16bc37-3e1f-410b-be03-7bec83dd6c33").ToByteArray();
        private const string DomainClientStorageContextKey = "8994c20a-179c-469c-a3b9-c4d6a8d2eb3c";

        private static readonly ThreeKeyValueStorage ClientStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(DomainClientStorageContextKey));

        /// <summary>
        /// Registers the domain as having access 
        /// </summary>
        public async Task<RedactedYouAuthDomainRegistration> RegisterDomainAsync(YouAuthDomainRegistrationRequest request,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();


            OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
            OdinValidationUtils.AssertNotNullOrEmpty(request.Domain, nameof(request.Domain));

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            if (null != await this.GetDomainRegistrationInternalAsync(new AsciiDomainName(request.Domain)))
            {
                throw new OdinClientException("Domain already registered");
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var grants = await circleMembershipService.CreateCircleGrantListAsync(keyStoreKey, request.CircleIds ?? new List<GuidId>(),
                masterKey, odinContext);

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

            await SaveRegistrationAsync(reg, odinContext);

            return reg.Redacted();
        }

        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(
            AsciiDomainName domain,
            string friendlyName,
            YouAuthDomainRegistrationRequest? request,
            IOdinContext odinContext)
        {
            OdinValidationUtils.AssertNotNullOrEmpty(friendlyName, nameof(friendlyName));
            odinContext.Caller.AssertHasMasterKey();

            var reg = await this.GetDomainRegistrationInternalAsync(domain);
            if (reg == null)
            {
                if (request == null)
                {
                    throw new OdinClientException($"{domain} not registered");
                }

                await RegisterDomainAsync(request, odinContext);
                reg = await GetDomainRegistrationInternalAsync(domain);
            }


            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = reg!.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var (accessRegistration, cat) = await exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.YouAuth);

            var expiresAt = request?.ConsentRequirements?.Expiration.milliseconds ?? UnixTimeUtc.Now().AddDays(365);
            var youAuthDomainClient = new YouAuthDomainClient(domain, friendlyName, accessRegistration)
            {
                TimeToLiveSeconds = Math.Max(0, (long)(expiresAt - UnixTimeUtc.Now()).TotalSeconds)
            };
            await SaveClientAsync(youAuthDomainClient);
            return (cat, reg.CorsHostName);
        }

        public async Task<RedactedYouAuthDomainRegistration?> GetRegistration(AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var result = await GetDomainRegistrationInternalAsync(domain);
            return result?.Redacted();
        }

        /// <summary>
        /// Determines if the specified domain requires consent from the owner before ...
        /// </summary>
        public async Task<bool> IsConsentRequiredAsync(AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            if (await circleNetworkService.IsConnectedAsync((OdinId)domain.DomainName, odinContext))
            {
                return false;
            }

            var reg = await this.GetDomainRegistrationInternalAsync(domain);

            return reg?.ConsentRequirements?.IsRequired() ?? true;
        }

        public async Task UpdateConsentRequirements(AsciiDomainName domain, ConsentRequirements consentRequirements,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            consentRequirements.Validate();

            var domainReg = await this.GetDomainRegistrationInternalAsync(domain);
            if (null == domainReg)
            {
                throw new OdinClientException("Domain not registered");
            }

            domainReg.ConsentRequirements = consentRequirements;

            await SaveRegistrationAsync(domainReg, odinContext);
            await ResetPermissionContextCacheAsync();
        }

        public async Task RevokeDomainAsync(AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var domainReg = await this.GetDomainRegistrationInternalAsync(domain);
            if (null == domainReg)
            {
                return;
            }

            //TODO: do we do anything with storage DEK here?
            domainReg.IsRevoked = true;
            await SaveRegistrationAsync(domainReg, odinContext);
            await ResetPermissionContextCacheAsync();
        }

        public async Task RemoveDomainRevocationAsync(AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var domainReg = await this.GetDomainRegistrationInternalAsync(domain);
            if (null == domainReg)
            {
                return;
            }

            //TODO: do we do anything with storage DEK here?
            domainReg.IsRevoked = false;
            await SaveRegistrationAsync(domainReg, odinContext);
            await ResetPermissionContextCacheAsync();
        }

        public async Task<List<RedactedYouAuthDomainClient>> GetRegisteredClientsAsync(AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var list = await ClientStorage.GetByCategoryAsync<YouAuthDomainClient>(db.KeyThreeValueCached, ClientDataType);
            var resp = list.Where(d => d.Domain.DomainName.ToLower() == domain.DomainName.ToLower()).Select(domainClient =>
                new RedactedYouAuthDomainClient()
                {
                    Domain = domainClient.Domain,
                    AccessRegistrationId = domainClient.AccessRegistration.Id,
                    FriendlyName = domainClient.FriendlyName,
                    IsRevoked = domainClient.AccessRegistration.IsRevoked,
                    Created = domainClient.AccessRegistration.Created,
                    AccessRegistrationClientType = domainClient.AccessRegistration.AccessRegistrationClientType
                }).ToList();

            return resp;
        }

        /// <summary>
        /// Deletes the current client calling into the system.  This is used to 'logout' an domain
        /// </summary>
        public async Task DeleteCurrentYouAuthDomainClientAsync(IOdinContext odinContext)
        {
            var context = odinContext;
            var accessRegistrationId = context.Caller.OdinClientContext?.AccessRegistrationId;

            var validAccess = accessRegistrationId != null &&
                              context.Caller.SecurityLevel == SecurityGroupType.Owner;

            if (!validAccess)
            {
                throw new OdinSecurityException("Invalid call to Delete domain client");
            }

            var client = await ClientStorage.GetAsync<YouAuthDomainClient>(db.KeyThreeValueCached, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await ClientStorage.DeleteAsync(db.KeyThreeValueCached, accessRegistrationId);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var client = await ClientStorage.GetAsync<YouAuthDomainClient>(db.KeyThreeValueCached, accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await ClientStorage.DeleteAsync(db.KeyThreeValueCached, accessRegistrationId);
        }

        public async Task DeleteDomainRegistrationAsync(AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var reg = await GetDomainRegistrationInternalAsync(domain);

            if (null == reg)
            {
                throw new OdinClientException("Invalid domain", OdinClientErrorCode.DomainNotRegistered);
            }

            //delete the clients
            var clientsByDomain =
                await ClientStorage.GetByDataTypeAsync<YouAuthDomainClient>(db.KeyThreeValueCached, GetDomainKey(domain).ToByteArray());

            await using var tx = await db.BeginStackedTransactionAsync();
            foreach (var c in clientsByDomain)
            {
                await ClientStorage.DeleteAsync(db.KeyThreeValueCached, c.AccessRegistration.Id);
            }

            await DomainStorage.DeleteAsync(db.KeyThreeValueCached, GetDomainKey(domain));
            tx.Commit();
        }

        public async Task<List<RedactedYouAuthDomainRegistration>> GetRegisteredDomainsAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var domains = await DomainStorage.GetByCategoryAsync<YouAuthDomainRegistration>(db.KeyThreeValueCached,
                DomainRegistrationDataType);
            var redactedList = domains.Select(d => d.Redacted()).ToList();
            return redactedList;
        }

        /// <summary>
        /// Gives access to all resource granted by the specified circle to the YouAuthDomain
        /// </summary>
        public async Task GrantCircleAsync(GuidId circleId, AsciiDomainName domainName, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var registration = await this.GetDomainRegistrationInternalAsync(domainName);

            if (registration == null)
            {
                throw new OdinSecurityException($"{domainName} is not registered");
            }

            if (registration.CircleGrants.TryGetValue(circleId, out _))
            {
                //TODO: Here we should ensure it's in the _circleMemberStorage just in case this was called because it's out of sync
                throw new OdinClientException($"{domainName} is already member of circle",
                    OdinClientErrorCode.IdentityAlreadyMemberOfCircle);
            }

            var circleDefinition = await circleMembershipService.GetCircleAsync(circleId, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = registration.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var circleGrant = await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDefinition, masterKey, odinContext);

            registration.CircleGrants.Add(circleGrant.CircleId, circleGrant);

            keyStoreKey.Wipe();

            await SaveRegistrationAsync(registration, odinContext);

            await ResetPermissionContextCacheAsync();
        }

        /// <summary>
        /// Removes drives and permissions of the specified circle from the odinId
        /// </summary>
        public async Task RevokeCircleAccess(GuidId circleId, AsciiDomainName domain, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var registration = await this.GetDomainRegistrationInternalAsync(domain);
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

            await SaveRegistrationAsync(registration, odinContext);

            await ResetPermissionContextCacheAsync();
        }

        // 

        public async Task<IOdinContext?> GetDotYouContextAsync(ClientAuthenticationToken token, IOdinContext currentOdinContext)
        {
            async Task<IOdinContext?> Creator()
            {
                var (isValid, accessReg, domainRegistration) = await ValidateClientAuthTokenAsync(token);

                if (!isValid || null == domainRegistration || accessReg == null)
                {
                    throw new OdinSecurityException("Invalid token");
                }

                //
                // If the domain is from an odin identity that is connected, upgrade their permissions
                //
                var odinId = (OdinId)domainRegistration.Domain.DomainName;
                var odinContext =
                    await circleNetworkService.TryCreateConnectedYouAuthContextAsync(odinId, token, accessReg, currentOdinContext);
                if (null != odinContext)
                {
                    return odinContext;
                }

                return await CreateAuthenticatedContextForYouAuthDomainAsync(token, domainRegistration, accessReg, currentOdinContext);
            }

            var result = await cache.GetOrAddContextAsync(token, Creator);
            return result;
        }

        private async Task<(bool isValid, AccessRegistration? accessReg, YouAuthDomainRegistration? youAuthDomainRegistration)>
            ValidateClientAuthTokenAsync(
                ClientAuthenticationToken authToken)
        {
            var domainClient = await ClientStorage.GetAsync<YouAuthDomainClient>(db.KeyThreeValueCached, authToken.Id);
            if (null == domainClient)
            {
                return (false, null, null);
            }

            var reg = await this.GetDomainRegistrationInternalAsync(domainClient.Domain);

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

        private async Task SaveClientAsync(YouAuthDomainClient youAuthDomainClient)
        {
            await ClientStorage.UpsertAsync(db.KeyThreeValueCached, youAuthDomainClient.AccessRegistration.Id,
                GetDomainKey(youAuthDomainClient.Domain).ToByteArray(),
                ClientDataType,
                youAuthDomainClient);
        }

        private async Task<YouAuthDomainRegistration?> GetDomainRegistrationInternalAsync(AsciiDomainName domain)
        {
            var key = GuidId.FromString(domain.DomainName);
            var reg = await DomainStorage.GetAsync<YouAuthDomainRegistration>(db.KeyThreeValueCached, key);

            if (null != reg)
            {
                //get the circle grants for this domain
                var circles = await circleMembershipService.GetCirclesGrantsByDomainAsync(reg.Domain, DomainType.YouAuth);
                reg.CircleGrants = circles.ToDictionary(cg => cg.CircleId.Value, cg => cg);
            }

            return reg;
        }

        /// <summary>
        /// Empties the cache and creates a new instance that can be built
        /// </summary>
        private async Task ResetPermissionContextCacheAsync()
        {
            await cache.ResetAsync();
        }

        private Guid GetDomainKey(AsciiDomainName domainName)
        {
            return GuidId.FromString(domainName.DomainName);
        }

        private async Task SaveRegistrationAsync(YouAuthDomainRegistration registration, IOdinContext odinContext)
        {
            var domain = new OdinId(registration.Domain);

            await using var tx = await db.BeginStackedTransactionAsync();

            //TODO: this is causing an issue where in the circles are also deleted for the ICR 
            // 
            await circleMembershipService.DeleteMemberFromAllCirclesAsync(registration.Domain, DomainType.YouAuth);

            foreach (var (circleId, circleGrant) in registration.CircleGrants)
            {
                var circleMembers = (await circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext))
                    .Where(d => d.DomainType == DomainType.YouAuth);
                var isMember = circleMembers.Any(d =>
                    OdinId.ToHashId(d.Domain) == OdinId.ToHashId(registration.Domain));

                if (!isMember)
                {
                    //
                    // BUG - this method calls upsert which will overwrite a circle membership
                    // from the DomainType.Identity when it is granted to the DomainType.youauth
                    //
                    await circleMembershipService.AddCircleMemberAsync(circleId, domain, circleGrant, DomainType.YouAuth);
                }
            }

            //clear them here so we don't have two locations
            registration.CircleGrants.Clear();

            await DomainStorage.UpsertAsync(db.KeyThreeValueCached, GetDomainKey(registration.Domain), GuidId.Empty,
                DomainRegistrationDataType,
                registration);

            tx.Commit();
        }

        private async Task<IOdinContext> CreateAuthenticatedContextForYouAuthDomainAsync(
            ClientAuthenticationToken authToken,
            YouAuthDomainRegistration domainRegistration,
            AccessRegistration accessReg,
            IOdinContext odinContext)
        {
            if (!string.IsNullOrEmpty(domainRegistration.CorsHostName))
            {
                //just in case something changed in the _db.KeyThreeValue record
                AppUtil.AssertValidCorsHeader(domainRegistration.CorsHostName);
            }

            //TODO: do we want allow youauthdomains to have these permissions?

            var permissionKeys = tenantContext.Settings.GetAdditionalPermissionKeysForAuthenticatedIdentities();
            var anonymousDrivePermissions = tenantContext.Settings.GetAnonymousDrivePermissionsForAuthenticatedIdentities();

            var (grants, enabledCircles) = await
                circleMembershipService.MapCircleGrantsToExchangeGrantsAsync(domainRegistration.Domain,
                    domainRegistration.CircleGrants.Values.ToList(),
                    odinContext);

            var permissionContext = await exchangeGrantService.CreatePermissionContext(
                authToken: authToken,
                grants: grants,
                accessReg: accessReg,
                odinContext: odinContext,
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