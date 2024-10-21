using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Core.Time;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
using Odin.Services.Mediator.Owner;
using Odin.Services.Membership.Connections;
using Odin.Services.Registry;
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
        private readonly OwnerSecretService _secretService;
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OdinConfiguration _configuration;

        private readonly IIdentityRegistry _identityRegistry;
        private readonly OdinContextCache _cache;
        private readonly ILogger<OwnerAuthenticationService> _logger;
        private readonly DriveManager _driveManager;
        private readonly TenantContext _tenantContext;
        private readonly IcrKeyService _icrKeyService;
        private readonly TenantConfigService _tenantConfigService;
        private readonly IHttpContextAccessor _httpContextAccessor;


        private readonly SingleKeyValueStorage _nonceDataStorage;
        private readonly SingleKeyValueStorage _serverTokenStorage;
        private readonly SingleKeyValueStorage _firstRunInfoStorage;

        public OwnerAuthenticationService(ILogger<OwnerAuthenticationService> logger, OwnerSecretService secretService,
            TenantSystemStorage tenantSystemStorage,
            TenantContext tenantContext, OdinConfiguration config, DriveManager driveManager, IcrKeyService icrKeyService,
            TenantConfigService tenantConfigService, IHttpContextAccessor httpContextAccessor, IIdentityRegistry identityRegistry,
            OdinConfiguration configuration)
        {
            _logger = logger;
            _secretService = secretService;
            _tenantSystemStorage = tenantSystemStorage;
            _tenantContext = tenantContext;
            _driveManager = driveManager;
            _icrKeyService = icrKeyService;
            _tenantConfigService = tenantConfigService;
            _httpContextAccessor = httpContextAccessor;
            _identityRegistry = identityRegistry;

            _configuration = configuration;

            //TODO: does this need to mwatch owner secret service?
            // const string nonceDataContextKey = "c45430e7-9c05-49fa-bc8b-d8c1f261f57e";
            const string nonceDataContextKey = "cc5430e7-cc05-49aa-bc8b-d8c1f261f5ee";
            _nonceDataStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(nonceDataContextKey));

            const string serverTokenContextKey = "72a58c43-4058-4773-8dd5-542992b8ef67";
            _serverTokenStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(serverTokenContextKey));

            const string firstRunContextKey = "c05d8c71-e75f-4998-ad74-7e94d8752b56";
            _firstRunInfoStorage = tenantSystemStorage.CreateSingleKeyValueStorage(Guid.Parse(firstRunContextKey));

            _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        /// <summary>
        /// Generates a one time value to used when authenticating a user
        /// </summary>
        public async Task<NonceData> GenerateAuthenticationNonceAsync()
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var salts = await _secretService.GetStoredSaltsAsync(db);
            var (publicKeyCrc32C, publicKeyPem) = await _secretService.GetCurrentAuthenticationRsaKey(db);

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKeyPem, publicKeyCrc32C);

            await _nonceDataStorage.UpsertAsync(db, nonce.Id, nonce);

            return nonce;
        }

        /// <summary>
        /// Authenticates the owner based on the <see cref="PasswordReply"/> specified.
        /// </summary>
        /// <param name="reply"></param>
        /// <exception cref="OdinSecurityException">Thrown when a user cannot be authenticated</exception>
        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> AuthenticateAsync(PasswordReply reply)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var noncePackage = await AssertValidPasswordAsync(reply);

            //now that the password key matches, we set return the client auth token
            var keys = await this._secretService.GetOfflineRsaKeyList(db);
            var (clientToken, serverToken) = OwnerConsoleTokenManager.CreateToken(noncePackage, reply, keys);

            await _serverTokenStorage.UpsertAsync(db, serverToken.Id, serverToken);

            // TODO - where do we set the MasterKek and MasterDek?

            // TODO: audit login some where, or in helper class below

            var token = new ClientAuthenticationToken()
            {
                Id = serverToken.Id,
                AccessTokenHalfKey = new SensitiveByteArray(clientToken.GetKey()),
                ClientTokenType = ClientTokenType.Other
            };

            //set the odin context so the request of this request can use the master key (note: this was added so we could set keys on first login)
            var odinContext = _httpContextAccessor!.HttpContext!.RequestServices.GetRequiredService<IOdinContext>();
            await UpdateOdinContextAsync(token, odinContext);
            await EnsureFirstRunOperationsAsync(odinContext);

            return (token, serverToken.SharedSecret.ToSensitiveByteArray());
        }

        private async Task<NonceData> AssertValidPasswordAsync(PasswordReply reply)
        {
            var db = _tenantSystemStorage.IdentityDatabase;

            byte[] key = Convert.FromBase64String(reply.Nonce64);

            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = await _nonceDataStorage.GetAsync<NonceData>(db, new GuidId(key));

            // TODO TEST Make sure an exception is thrown if it does not exist.
            OdinValidationUtils.AssertNotNull(noncePackage, nameof(noncePackage));

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            await _nonceDataStorage.DeleteAsync(db, new GuidId(key));

            // Here we test if the client's provided nonce is saved on the server and if the
            // client's calculated nonceHash is equal to the same calculation on the server
            await _secretService.AssertPasswordKeyMatch(reply.NonceHashedPassword64, reply.Nonce64, db);
            return noncePackage;
        }

        /// <summary>
        /// Determines if the <paramref name="sessionTokenId"/> is valid and has not expired.  
        /// </summary>
        /// <param name="sessionTokenId">The token to be validated</param>
        /// <returns></returns>
        public async Task<bool> IsValidTokenAsync(Guid sessionTokenId)
        {
            var db = _tenantSystemStorage.IdentityDatabase;

            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            var entry = await _serverTokenStorage.GetAsync<OwnerConsoleToken>(db, sessionTokenId);
            return await Task.FromResult(IsAuthTokenEntryValid(entry));
        }

        /// <summary>
        /// Returns the LoginKek used to access the primary and application data encryption keys
        /// </summary>
        public async Task<(SensitiveByteArray, SensitiveByteArray)> GetMasterKeyAsync(Guid sessionTokenId, SensitiveByteArray clientSecret)
        {
            var db = _tenantSystemStorage.IdentityDatabase;

            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = await _serverTokenStorage.GetAsync<OwnerConsoleToken>(db, sessionTokenId);

            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new OdinClientException("Token is invalid", OdinClientErrorCode.InvalidAuthToken);
            }

            var mk = await _secretService.GetMasterKeyAsync(loginToken, clientSecret, db);

            //HACK: need to clone this here because the owner console token is getting wipe by the owner console token finalizer
            var len = loginToken.SharedSecret.Length;
            var clone = new byte[len];
            Buffer.BlockCopy(loginToken.SharedSecret, 0, clone, 0, len);

            loginToken.Dispose();
            return (mk, clone.ToSensitiveByteArray());
        }

        public async Task<(SensitiveByteArray masterKey, PermissionContext permissionContext)> GetPermissionContextAsync(ClientAuthenticationToken token,
            IOdinContext odinContext)
        {
            var db = _tenantSystemStorage.IdentityDatabase;

            if (await IsValidTokenAsync(token.Id))
            {
                var (masterKey, clientSharedSecret) = await GetMasterKeyAsync(token.Id, token.AccessTokenHalfKey);

                var icrKey = _icrKeyService.GetMasterKeyEncryptedIcrKey();

                var allDrives = await _driveManager.GetDrives(PageOptions.All, odinContext, db);
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
        public async Task<IOdinContext> GetDotYouContextAsync(ClientAuthenticationToken token, IOdinContext odinContext)
        {
            var creator = new Func<Task<IOdinContext>>(async delegate
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
                    odinClientContext: new OdinClientContext()
                    {
                        ClientIdOrDomain = string.Empty,
                        CorsHostName = string.Empty,
                        AccessRegistrationId = token.Id,
                        DevicePushNotificationKey = PushNotificationCookieUtil.GetDeviceKey(_httpContextAccessor!.HttpContext!.Request)
                    });

                return dotYouContext;
            });

            return await _cache.GetOrAddContext(token, creator);
        }

        /// <summary>
        /// Extends the token life by <param name="ttlSeconds"></param> if it is valid.
        /// </summary>
        /// <param name="tokenId"></param>
        /// <param name="ttlSeconds"></param>
        /// <param name="db"></param>
        public async Task ExtendTokenLifeAsync(Guid tokenId, int ttlSeconds)
        {
            var db = _tenantSystemStorage.IdentityDatabase;

            var entry = await GetValidatedEntry(tokenId);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            await _serverTokenStorage.UpsertAsync(db, entry.Id, entry);
        }

        /// <summary>
        /// Expires the <paramref name="tokenId"/> thus making it invalid.  This can be used when a user
        /// clicks logout.  Invalid or expired tokens are ignored.
        /// </summary>
        /// <param name="tokenId"></param>
        public async Task ExpireToken(Guid tokenId)
        {
            var db = _tenantSystemStorage.IdentityDatabase;

            await _serverTokenStorage.DeleteAsync(db, tokenId);
        }

        private async Task<OwnerConsoleToken> GetValidatedEntry(Guid tokenId)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var entry = await _serverTokenStorage.GetAsync<OwnerConsoleToken>(db, tokenId);
            AssertTokenIsValid(entry);
            return entry;
        }

        private bool IsAuthTokenEntryValid(OwnerConsoleToken entry)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var valid =
                null != entry &&
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

        public Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            //reset cache so the drive is reached on the next request
            if (notification.IsNewDrive)
            {
                _logger.LogDebug("New drive created [{0}]; Purging cache ", notification.Drive.TargetDriveInfo);
                _cache.Reset();
            }

            return Task.CompletedTask;
        }

        public async Task<bool> UpdateOdinContextAsync(ClientAuthenticationToken token, IOdinContext odinContext)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var context = _httpContextAccessor.HttpContext;
            odinContext.SetAuthContext(OwnerAuthConstants.SchemeName);

            //HACK: fix this
            //a bit of a hack here: we have to set the context as owner
            //because it's required to build the permission context
            // this is justified because we're heading down the owner api path
            // just below this, we check to see if the token was good.  if not, the call fails.
            odinContext.Caller = new CallerContext(
                odinId: (OdinId)context!.Request.Host.Host,
                masterKey: null, //will be set later
                securityLevel: SecurityGroupType.Owner,
                odinClientContext: new OdinClientContext()
                {
                    ClientIdOrDomain = string.Empty,
                    CorsHostName = string.Empty,
                    AccessRegistrationId = token.Id,
                    DevicePushNotificationKey = PushNotificationCookieUtil.GetDeviceKey(_httpContextAccessor!.HttpContext!.Request)
                });

            IOdinContext ctx = await this.GetDotYouContextAsync(token, odinContext);

            if (null == ctx)
            {
                return false;
            }

            //🐈⏰
            var catTime = SequentialGuid.ToUnixTimeUtc(token.Id);
            odinContext.AuthTokenCreated = catTime;

            odinContext.Caller = ctx.Caller;
            odinContext.SetPermissionContext(ctx.PermissionsContext);

            //experimental:tell the system the owner is online
            // var mediator = context.RequestServices.GetRequiredService<IMediator>();
            // await mediator.Publish(new OwnerIsOnlineNotification()
            // {
            // });

            return true;
        }

        //

        private async Task EnsureFirstRunOperationsAsync(IOdinContext odinContext)
        {
            var db = _tenantSystemStorage.IdentityDatabase;
            var fli = await _firstRunInfoStorage.GetAsync<FirstOwnerLoginInfo>(db, FirstOwnerLoginInfo.Key);
            if (fli == null)
            {
                await _tenantConfigService.CreateInitialKeysAsync(odinContext);

                await _firstRunInfoStorage.UpsertAsync(db, FirstOwnerLoginInfo.Key, new FirstOwnerLoginInfo()
                {
                    FirstLoginDate = UnixTimeUtc.Now()
                });
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