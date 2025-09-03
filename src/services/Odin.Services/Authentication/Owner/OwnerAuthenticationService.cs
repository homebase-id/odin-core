using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.Identity.Cache;
using Odin.Core.Time;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Membership.Connections;
using Odin.Services.Registry;
using Odin.Services.ShamiraPasswordRecovery;
using Odin.Services.Util;

// Goals here are that:
//   * the password never leaves the clients.
//   * the password hash changes with every login request, making playback impossible
//   * the private encryption key on the server is encrypted with a KEK
//   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
//   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
namespace Odin.Services.Authentication.Owner
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService : INotificationHandler<DriveDefinitionAddedNotification>
    {
        private const string NonceDataContextKey = "cc5430e7-cc05-49aa-bc8b-d8c1f261f5ee";

        private static readonly SingleKeyValueStorage NonceDataStorage =
            TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(NonceDataContextKey));

        private const string ServerTokenContextKey = "72a58c43-4058-4773-8dd5-542992b8ef67";

        private static readonly SingleKeyValueStorage ServerTokenStorage =
            TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(ServerTokenContextKey));

        private const string FirstRunContextKey = "c05d8c71-e75f-4998-ad74-7e94d8752b56";

        private static readonly SingleKeyValueStorage FirstRunInfoStorage =
            TenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(FirstRunContextKey));

        private readonly OwnerSecretService _secretService;

        private readonly OdinConfiguration _configuration;
        private readonly TableKeyValueCached _tblKeyValue;
        private readonly ShamirRecoveryService _shamirRecoveryService;

        private readonly IIdentityRegistry _identityRegistry;
        private readonly OdinContextCache _cache;
        private readonly ILogger<OwnerAuthenticationService> _logger;
        private readonly IDriveManager _driveManager;
        private readonly TenantContext _tenantContext;
        private readonly IcrKeyService _icrKeyService;
        private readonly TenantConfigService _tenantConfigService;

        public OwnerAuthenticationService(
            ILogger<OwnerAuthenticationService> logger,
            OwnerSecretService secretService,
            TenantContext tenantContext,
            OdinConfiguration config,
            IDriveManager driveManager,
            IcrKeyService icrKeyService,
            TenantConfigService tenantConfigService,
            IIdentityRegistry identityRegistry,
            OdinConfiguration configuration,
            TableKeyValueCached tblKeyValue,
            ShamirRecoveryService shamirRecoveryService,
            OdinContextCache cache)
        {
            _logger = logger;
            _secretService = secretService;

            _tenantContext = tenantContext;
            _driveManager = driveManager;
            _icrKeyService = icrKeyService;
            _tenantConfigService = tenantConfigService;
            _identityRegistry = identityRegistry;

            _configuration = configuration;
            _tblKeyValue = tblKeyValue;
            _shamirRecoveryService = shamirRecoveryService;

            _cache = cache;
        }

        /// <summary>
        /// Generates a one time value to used when authenticating a user
        /// </summary>
        public async Task<NonceData> GenerateAuthenticationNonceAsync()
        {
            var salts = await _secretService.GetStoredSaltsAsync();
            var (publicKeyCrc32C, publicKeyJwk) = await _secretService.GetCurrentAuthenticationEccKeyAsync();

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKeyJwk, publicKeyCrc32C);

            await NonceDataStorage.UpsertAsync(_tblKeyValue, nonce.Id, nonce);

            return nonce;
        }

        public async Task VerifyPasswordAsync(PasswordReply reply, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            _ = await this.AssertValidPasswordAsync(reply);
        }

        /// <summary>
        /// Authenticates the owner based on the <see cref="PasswordReply"/> specified.
        /// </summary>
        /// <param name="reply"></param>
        /// <param name="devicePushNotificationKey"></param>
        /// <param name="odinContext"></param>
        /// <exception cref="OdinSecurityException">Thrown when a user cannot be authenticated</exception>
        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> AuthenticateAsync(PasswordReply reply,
            Guid devicePushNotificationKey, IOdinContext odinContext)
        {
            var noncePackage = await AssertValidPasswordAsync(reply);

            //now that the password key matches, we set return the client auth token
            var keys = await this._secretService.GetOfflineEccKeyListAsync();
            var (clientToken, serverToken) = OwnerConsoleTokenManager.CreateToken(noncePackage, reply, keys);

            await ServerTokenStorage.UpsertAsync(_tblKeyValue, serverToken.Id, serverToken);

            // TODO: audit login some where, or in helper class below

            var token = new ClientAuthenticationToken()
            {
                Id = serverToken.Id,
                AccessTokenHalfKey = new SensitiveByteArray(clientToken.GetKey()),
                ClientTokenType = ClientTokenType.Other
            };

            var clientContext = new OdinClientContext()
            {
                ClientIdOrDomain = string.Empty,
                CorsHostName = string.Empty,
                AccessRegistrationId = token.Id,
                DevicePushNotificationKey = devicePushNotificationKey
            };

            //set the odin context so the request of this request can use the master key (note: this was added so we could set keys on first login)
            await this.UpdateOdinContextAsync(token, clientContext, odinContext);
            await EnsureFirstRunOperationsAsync(odinContext);

            await _shamirRecoveryService.ForceExitRecoveryMode(odinContext);

            return (token, serverToken.SharedSecret.ToSensitiveByteArray());
        }

        public async Task<NonceData> AssertValidPasswordAsync(PasswordReply reply)
        {
            byte[] key = Convert.FromBase64String(reply.Nonce64);

            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = await NonceDataStorage.GetAsync<NonceData>(_tblKeyValue, new GuidId(key));

            // TODO TEST Make sure an exception is thrown if it does not exist.
            OdinValidationUtils.AssertNotNull(noncePackage, nameof(noncePackage));

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            await NonceDataStorage.DeleteAsync(_tblKeyValue, new GuidId(key));

            // Here we test if the client's provided nonce is saved on the server and if the
            // client's calculated nonceHash is equal to the same calculation on the server
            await _secretService.AssertPasswordKeyMatchAsync(reply.NonceHashedPassword64, reply.Nonce64);
            return noncePackage;
        }

        /// <summary>
        /// Determines if the <paramref name="sessionTokenId"/> is valid and has not expired.  
        /// </summary>
        /// <param name="sessionTokenId">The token to be validated</param>
        /// <returns></returns>
        public async Task<bool> IsValidTokenAsync(Guid sessionTokenId)
        {
            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            var entry = await ServerTokenStorage.GetAsync<OwnerConsoleToken>(_tblKeyValue, sessionTokenId);
            return IsAuthTokenEntryValid(entry);
        }

        /// <summary>
        /// Returns the LoginKek used to access the primary and application data encryption keys
        /// </summary>
        public async Task<(SensitiveByteArray, SensitiveByteArray)> GetMasterKeyAsync(Guid sessionTokenId, SensitiveByteArray clientSecret)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = await ServerTokenStorage.GetAsync<OwnerConsoleToken>(_tblKeyValue, sessionTokenId);

            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new OdinClientException("Token is invalid", OdinClientErrorCode.InvalidAuthToken);
            }

            var mk = await _secretService.GetMasterKeyAsync(loginToken, clientSecret);

            //HACK: need to clone this here because the owner console token is getting wipe by the owner console token finalizer
            var len = loginToken.SharedSecret.Length;
            var clone = new byte[len];
            Buffer.BlockCopy(loginToken.SharedSecret, 0, clone, 0, len);

            loginToken.Dispose();
            return (mk, clone.ToSensitiveByteArray());
        }

        public async Task<(SensitiveByteArray masterKey, PermissionContext permissionContext)> GetPermissionContextAsync(
            ClientAuthenticationToken token,
            IOdinContext odinContext)
        {
            if (await IsValidTokenAsync(token.Id))
            {
                var (masterKey, clientSharedSecret) = await GetMasterKeyAsync(token.Id, token.AccessTokenHalfKey);

                var icrKey = await _icrKeyService.GetMasterKeyEncryptedIcrKeyAsync();

                var allDrives = await _driveManager.GetDrivesAsync(PageOptions.All, odinContext);
                var allDriveGrants = allDrives.Results.Select(d => new DriveGrant()
                {
                    DriveId = d.Id,
                    KeyStoreKeyEncryptedStorageKey = d.MasterKeyEncryptedStorageKey,
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = d.TargetDriveInfo,
                        Permission = DrivePermission.All
                    },
                });

                var permissionGroupMap = new Dictionary<string, PermissionGroup>
                {
                    { "owner_grants", new PermissionGroup(new PermissionSet(PermissionKeys.All), allDriveGrants, masterKey, icrKey) },
                };

                var ctx = new PermissionContext(permissionGroupMap, clientSharedSecret);

                return (masterKey, ctx);
            }

            throw new OdinSecurityException("Invalid owner token");
        }

        /// <summary>
        /// Gets the <see cref="OdinContext"/> for the specified token from cache or disk.
        /// </summary>
        public async Task<IOdinContext> GetDotYouContextAsync(ClientAuthenticationToken token, OdinClientContext clientContext,
            IOdinContext odinContext)
        {
            var creator = new Func<Task<IOdinContext>>(async () =>
            {
                var dotYouContext = new OdinContext();
                var (masterKey, permissionContext) = await GetPermissionContextAsync(token, odinContext);

                if (null == permissionContext || masterKey.IsEmpty())
                {
                    throw new OdinSecurityException("Invalid owner token");
                }

                dotYouContext.SetPermissionContext(permissionContext);

                dotYouContext.Caller = new CallerContext(
                    odinId: _tenantContext.HostOdinId,
                    masterKey: masterKey,
                    securityLevel: SecurityGroupType.Owner,
                    odinClientContext: clientContext);

                return dotYouContext;
            });

            return await _cache.GetOrAddContextAsync(token, creator);
        }

        /// <summary>
        /// Extends the token life by <param name="ttlSeconds"></param> if it is valid.
        /// </summary>
        /// <param name="tokenId"></param>
        /// <param name="ttlSeconds"></param>
        public async Task ExtendTokenLifeAsync(Guid tokenId, int ttlSeconds)
        {
            var entry = await GetValidatedEntryAsync(tokenId);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            await ServerTokenStorage.UpsertAsync(_tblKeyValue, entry.Id, entry);
        }

        /// <summary>
        /// Expires the <paramref name="tokenId"/> thus making it invalid.  This can be used when a user
        /// clicks logout.  Invalid or expired tokens are ignored.
        /// </summary>
        /// <param name="tokenId"></param>
        public async Task ExpireTokenAsync(Guid tokenId)
        {
            await ServerTokenStorage.DeleteAsync(_tblKeyValue, tokenId);
        }

        private async Task<OwnerConsoleToken> GetValidatedEntryAsync(Guid tokenId)
        {
            var entry = await ServerTokenStorage.GetAsync<OwnerConsoleToken>(_tblKeyValue, tokenId);
            AssertTokenIsValid(entry);
            return entry;
        }

        private bool IsAuthTokenEntryValid(OwnerConsoleToken entry)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var valid = null != entry &&
                        entry.Id != Guid.Empty &&
                        entry.ExpiryUnixTime > now;

            return valid;
        }

        private void AssertTokenIsValid(OwnerConsoleToken entry)
        {
            if (IsAuthTokenEntryValid(entry) == false)
            {
                throw new OdinSecurityException("Auth token entry is invalid");
            }
        }

        public async Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            //reset cache so the drive is reached on the next request
            if (notification.IsNewDrive)
            {
                _logger.LogDebug("New drive created [{0}]; Purging cache ", notification.Drive.TargetDriveInfo);
                await _cache.ResetAsync();
            }
        }

        public async Task<bool> UpdateOdinContextAsync(ClientAuthenticationToken token, OdinClientContext clientContext,
            IOdinContext odinContext)
        {
            odinContext.SetAuthContext(OwnerAuthConstants.SchemeName);

            //HACK: fix this
            //a bit of a hack here: we have to set the context as owner
            //because it's required to build the permission context
            // this is justified because we're heading down the owner api path
            // just below this, we check to see if the token was good.  if not, the call fails.
            odinContext.Caller = new CallerContext(
                odinId: _tenantContext.HostOdinId,
                masterKey: null, //will be set later
                securityLevel: SecurityGroupType.Owner,
                odinClientContext: clientContext ?? new OdinClientContext
                {
                    CorsHostName = null,
                    AccessRegistrationId = null,
                    DevicePushNotificationKey = null,
                    ClientIdOrDomain = null
                });

            IOdinContext ctx = await this.GetDotYouContextAsync(token, clientContext, odinContext);

            if (null == ctx)
            {
                return false;
            }

            //🐈⏰
            var catTime = SequentialGuid.ToUnixTimeUtc(token.Id);
            odinContext.AuthTokenCreated = catTime;

            odinContext.Caller = ctx.Caller;
            odinContext.SetPermissionContext(ctx.PermissionsContext);

            return true;
        }

        //

        private async Task EnsureFirstRunOperationsAsync(IOdinContext odinContext)
        {
            var fli = await FirstRunInfoStorage.GetAsync<FirstOwnerLoginInfo>(_tblKeyValue, FirstOwnerLoginInfo.Key);
            if (fli == null)
            {
                await _tenantConfigService.CreateInitialKeysAsync(odinContext);

                await FirstRunInfoStorage.UpsertAsync(_tblKeyValue, FirstOwnerLoginInfo.Key, new FirstOwnerLoginInfo()
                {
                    FirstLoginDate = UnixTimeUtc.Now()
                });

                // put new identity on latest version of data from the get go so upgrades dont run                
                await _tenantConfigService.ForceVersionNumberAsync(Version.DataVersionNumber);
            }
        }

        public async Task MarkForDeletionAsync(PasswordReply currentPasswordReply, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var _ = await this.AssertValidPasswordAsync(currentPasswordReply);
            await _identityRegistry.MarkForDeletionAsync(_tenantContext.HostOdinId);

            var tc = _identityRegistry.CreateTenantContext(_tenantContext.HostOdinId);
            tc.Update(tc);
        }

        public async Task UnmarkForDeletionAsync(PasswordReply currentPasswordReply, IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var _ = await this.AssertValidPasswordAsync(currentPasswordReply);
            await _identityRegistry.UnmarkForDeletionAsync(_tenantContext.HostOdinId);

            var tc = _identityRegistry.CreateTenantContext(_tenantContext.HostOdinId);
            tc.Update(tc);
        }

        public async Task<AccountStatusResponse> GetAccountStatusAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            var idReg = await _identityRegistry.GetAsync(_tenantContext.HostOdinId);

            return new AccountStatusResponse()
            {
                PlannedDeletionDate = idReg.MarkedForDeletionDate.HasValue
                    ? idReg.MarkedForDeletionDate.Value.AddDays(_configuration.Registry.DaysUntilAccountDeletion)
                    : null,
                PlanId = idReg.PlanId,
            };
        }
    }
}