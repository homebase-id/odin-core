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
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.Identity.Wrappers;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.Authorization;
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
        OdinContextCache cache,
        ClientRegistrationStorage clientRegistrationStorage)
    {
        private static readonly byte[] DomainRegistrationDataType = Guid.Parse("0c2c70c2-86e9-4214-818d-8b57c8d59762").ToByteArray();
        private const string DomainStorageContextKey = "e11ff091-0edf-4532-8b0f-b9d9ebe0880f";

        private static readonly ThreeKeyValueStorage DomainStorage =
            TenantSystemStorage.CreateThreeKeyValueStorage(Guid.Parse(DomainStorageContextKey));

        // Domain clients live in the client-registrations table, like every other client token: it
        // is the store that enforces expiry. They used to sit in a key-value store that did not;
        // TableClientRegistrationsMigrationV202609241200 carried those across at server start.

        /// <summary>
        /// Registers the domain as having access 
        /// </summary>
        public async Task<RedactedYouAuthDomainRegistration> RegisterDomainAsync(YouAuthDomainRegistrationRequest request,
            IOdinContext odinContext)
        {
            return await TryRegisterDomainAsync(request, odinContext) ?? throw new OdinClientException("Domain already registered");
        }

        /// <summary>
        /// <see cref="RegisterDomainAsync"/>, or null when the domain is registered already, by an earlier call or one
        /// running at the same time: the write is insert-only, so the key store key the first one made, and every
        /// client minted from it, stays.
        /// </summary>
        public async Task<RedactedYouAuthDomainRegistration?> TryRegisterDomainAsync(YouAuthDomainRegistrationRequest request,
            IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
            OdinValidationUtils.AssertNotNullOrEmpty(request.Domain, nameof(request.Domain));

            if (!string.IsNullOrEmpty(request.CorsHostName))
            {
                AppUtil.AssertValidCorsHeader(request.CorsHostName);
            }

            var domain = new AsciiDomainName(request.Domain);
            if (null != await this.GetDomainRegistrationInternalAsync(domain))
            {
                return null;
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var grants = await circleMembershipService.CreateCircleGrantListAsync(keyStoreKey, request.CircleIds ?? new List<GuidId>(),
                new MasterKeyStorageKeySource(masterKey), odinContext);

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

            if (!await SaveRegistrationAsync(reg, odinContext, isNew: true))
            {
                return null;
            }

            return reg.Redacted();
        }

        /// <param name="publishedName">
        /// The name the domain published for itself (docs/youauth-client-metadata-plan.md), when the
        /// caller has fetched one. It becomes the registration's name if that differs: consent
        /// registers a domain under its host name before the authorize flow, which has the verified
        /// name, gets here. Null leaves the name alone.
        /// </param>
        public async Task<(ClientAccessToken cat, string corsHostName)> RegisterClientAsync(
            AsciiDomainName domain,
            string friendlyName,
            YouAuthDomainRegistrationRequest? request,
            IOdinContext odinContext,
            string? publishedName = null)
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

                // A consent page and a sign-in at once both get here; the one registering second uses the first's
                await TryRegisterDomainAsync(request, odinContext);
                reg = await GetDomainRegistrationInternalAsync(domain)
                      ?? throw new OdinClientException($"{domain} not registered");
            }

            if (publishedName != null && reg.Name != publishedName)
            {
                reg.Name = publishedName;
                await SaveRegistrationAsync(reg, odinContext);
            }

            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = reg.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var (accessRegistration, cat) = await exchangeGrantService.CreateClientAccessToken(keyStoreKey, ClientTokenType.YouAuth);

            // The stored registration's consent, not the request's: the owner's choice is on the
            // registration by the time a client is issued, and the request is only there for a
            // domain being registered on the way past.
            var youAuthDomainClient = YouAuthDomainClient.Create(domain, friendlyName, accessRegistration, reg.ConsentRequirements);
            await clientRegistrationStorage.SaveAsync(youAuthDomainClient);
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

            var list = await GetClientsByDomainAsync(domain);
            var resp = list.Select(domainClient =>
                new RedactedYouAuthDomainClient()
                {
                    Domain = domainClient.Domain,
                    AccessRegistrationId = domainClient.ServerHalfOfClientKey.Id,
                    FriendlyName = domainClient.FriendlyName,
                    IsRevoked = domainClient.ServerHalfOfClientKey.IsRevoked,
                    Created = domainClient.ServerHalfOfClientKey.Created,
                    AccessRegistrationClientType = domainClient.ServerHalfOfClientKey.AccessRegistrationClientType
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

            await DeleteClientOrThrowAsync(accessRegistrationId);
        }

        public async Task DeleteClientAsync(GuidId accessRegistrationId, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            await DeleteClientOrThrowAsync(accessRegistrationId);
        }

        private async Task DeleteClientOrThrowAsync(Guid accessRegistrationId)
        {
            var client = await clientRegistrationStorage.GetAsync<YouAuthDomainClient>(accessRegistrationId);

            if (null == client)
            {
                throw new OdinClientException("Invalid access reg id", OdinClientErrorCode.InvalidAccessRegistrationId);
            }

            await clientRegistrationStorage.DeleteAsync(accessRegistrationId);
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
            var clientsByDomain = await GetClientsByDomainAsync(domain);

            await using var tx = await db.BeginStackedTransactionAsync();
            await clientRegistrationStorage.DeleteManyAsync(clientsByDomain.Select(c => c.ServerHalfOfClientKey.Id.Value));

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

            if (await IsConnectedIdentityAsync(registration.Domain))
            {
                throw new OdinClientException($"{domainName} is a connected identity; grant the circle to the connection",
                    OdinClientErrorCode.ArgumentError);
            }

            var circleDefinition = await circleMembershipService.GetCircleAsync(circleId, odinContext);
            var masterKey = odinContext.Caller.GetMasterKey();
            var keyStoreKey = registration.MasterKeyEncryptedKeyStoreKey.DecryptKeyClone(masterKey);
            var circleGrant = await circleMembershipService.CreateCircleGrantAsync(keyStoreKey, circleDefinition,
                new MasterKeyStorageKeySource(masterKey), odinContext);

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
            // Runs on a cache miss only, so what it does to the row -- restart a sliding lifetime --
            // happens at most once per cache lifetime per token, and ExtendLife itself skips the
            // write when the date would move by less than a day.
            async Task<(IOdinContext?, TimeSpan?)> Creator()
            {
                if (await ValidateClientAuthTokenAsync(token) is not var (domainClient, row, domainRegistration))
                {
                    throw new OdinSecurityException("Invalid token");
                }

                if (domainClient.SlidingExpiration)
                {
                    await clientRegistrationStorage.ExtendLife(row);
                }

                // The cached context must not outlive the token, whichever way its date was set.
                var cacheFor = row.expiresAt - UnixTimeUtc.Now();

                var accessReg = domainClient.ServerHalfOfClientKey;

                //
                // If the domain is from an odin identity that is connected, upgrade their permissions
                //
                var odinId = (OdinId)domainRegistration.Domain.DomainName;
                var odinContext =
                    await circleNetworkService.TryCreateConnectedYouAuthContextAsync(odinId, token, accessReg, currentOdinContext);
                if (null != odinContext)
                {
                    return (odinContext, cacheFor);
                }

                var context = await CreateAuthenticatedContextForYouAuthDomainAsync(token, domainRegistration, accessReg, currentOdinContext);
                return (context, cacheFor);
            }

            var result = await cache.GetOrAddContextAsync(token, Creator);
            return result;
        }

        /// <summary>
        /// The client behind a token, with its row and domain, when it is live, unrevoked, and under
        /// an unrevoked domain; null otherwise.
        /// </summary>
        private async Task<(YouAuthDomainClient client, ClientRegistrationsRecord row, YouAuthDomainRegistration registration)?>
            ValidateClientAuthTokenAsync(ClientAuthenticationToken authToken)
        {
            var (domainClient, row) = await clientRegistrationStorage.GetWithRowAsync<YouAuthDomainClient>(authToken.Id);
            if (domainClient == null || row == null)
            {
                return null;
            }

            var reg = await this.GetDomainRegistrationInternalAsync(domainClient.Domain);
            if (reg == null || domainClient.ServerHalfOfClientKey.IsRevoked || reg.IsRevoked)
            {
                return null;
            }

            return (domainClient, row, reg);
        }

        // 

        private Task<List<YouAuthDomainClient>> GetClientsByDomainAsync(AsciiDomainName domain)
        {
            // IssuedTo is the domain, already lower-cased by AsciiDomainName, so this is an exact match.
            return clientRegistrationStorage.GetByTypeAndIssuedToAsync<YouAuthDomainClient>(YouAuthDomainClient.CatType, domain.DomainName);
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

        /// <summary>
        /// Writes the registration and its circle rows.  A domain that is a connected identity gets no circle rows of its
        /// own: an identity is an identity, so its sign-in uses the connection's circles
        /// (<see cref="CircleNetworkService.TryCreateConnectedYouAuthContextAsync"/>), and one row per identity per
        /// circle means a YouAuth grant can never take over the connection's (#2043).  Any other domain's rows are
        /// matched to its grants, leaving alone a circle whose row an identity holds.
        /// </summary>
        /// <param name="isNew">Insert-only: nothing is written when the domain is registered already</param>
        /// <returns>False when <paramref name="isNew"/> and the domain was registered already.</returns>
        private async Task<bool> SaveRegistrationAsync(YouAuthDomainRegistration registration, IOdinContext odinContext,
            bool isNew = false)
        {
            var isConnected = await IsConnectedIdentityAsync(registration.Domain);
            var circleGrants = new Dictionary<Guid, CircleGrant>(registration.CircleGrants);

            //clear them here so we don't have two locations
            registration.CircleGrants.Clear();

            await using var tx = await db.BeginStackedTransactionAsync();

            // The registration first, so a domain registered already returns before any circle row is written
            var key = GetDomainKey(registration.Domain);
            if (isNew)
            {
                if (!await DomainStorage.TryInsertAsync(db.KeyThreeValueCached, key, GuidId.Empty, DomainRegistrationDataType,
                        registration))
                {
                    return false;
                }
            }
            else
            {
                await DomainStorage.UpsertAsync(db.KeyThreeValueCached, key, GuidId.Empty, DomainRegistrationDataType, registration);
            }

            if (isConnected)
            {
                await circleMembershipService.DeleteMemberFromAllCirclesAsync(registration.Domain, DomainType.YouAuth);
            }
            else
            {
                await circleMembershipService.ReconcileMemberCirclesAsync(registration.Domain, DomainType.YouAuth, circleGrants);
            }

            tx.Commit();
            return true;
        }

        /// <summary>Whether the domain is a connected identity: the connection's status only, not its grants.</summary>
        private async Task<bool> IsConnectedIdentityAsync(AsciiDomainName domain) =>
            (await db.ConnectionsCached.GetAsync((OdinId)domain.DomainName))?.status == (int)ConnectionStatus.Connected;

        private async Task<IOdinContext> CreateAuthenticatedContextForYouAuthDomainAsync(
            ClientAuthenticationToken authToken,
            YouAuthDomainRegistration domainRegistration,
            ServerHalfOfClientKey accessReg,
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