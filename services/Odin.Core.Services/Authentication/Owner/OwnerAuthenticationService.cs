using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Contacts.Circle.Membership;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Mediator;
using Odin.Core.Services.Mediator.Owner;
using Odin.Core.Time;
using Org.BouncyCastle.Asn1.Ocsp;

/// <summary>
/// Goals here are that:
///   * the password never leaves the clients.
///   * the password hash changes with every login request, making playback impossible
///   * the private encryption key on the server is encrypted with a KEK
///   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
///   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
/// </summary>
namespace Odin.Core.Services.Authentication.Owner
{
    /// <summary>
    /// Methods use for logging into the admin client of an Individual's DigitalIdentity
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService : INotificationHandler<DriveDefinitionAddedNotification>
    {
        private readonly TenantSystemStorage _tenantSystemStorage;
        private readonly OwnerSecretService _secretService;

        private readonly OdinContextCache _cache;
        private readonly ILogger<OwnerAuthenticationService> _logger;
        private readonly DriveManager _driveManager;
        private readonly TenantContext _tenantContext;
        private readonly IcrKeyService _icrKeyService;
        private readonly TenantConfigService _tenantConfigService;
        private readonly IHttpContextAccessor _httpContextAccessor;


        public OwnerAuthenticationService(ILogger<OwnerAuthenticationService> logger, OwnerSecretService secretService,
            TenantSystemStorage tenantSystemStorage,
            TenantContext tenantContext, OdinConfiguration config, DriveManager driveManager, IcrKeyService icrKeyService,
            TenantConfigService tenantConfigService, IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _secretService = secretService;
            _tenantSystemStorage = tenantSystemStorage;
            _tenantContext = tenantContext;
            _driveManager = driveManager;
            _icrKeyService = icrKeyService;
            _tenantConfigService = tenantConfigService;
            _httpContextAccessor = httpContextAccessor;

            _cache = new OdinContextCache(config.Host.CacheSlidingExpirationSeconds);
        }

        /// <summary>
        /// Generates a one time value to used when authenticating a user
        /// </summary>
        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();
            var (publicKeyCrc32C, publicKeyPem) = await _secretService.GetCurrentAuthenticationRsaKey();

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKeyPem, publicKeyCrc32C);
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(nonce.Id, nonce);
            return nonce;
        }

        /// <summary>
        /// Authenticates the owner based on the <see cref="PasswordReply"/> specified.
        /// </summary>
        /// <param name="reply"></param>
        /// <exception cref="OdinSecurityException">Thrown when a user cannot be authenticated</exception>
        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> Authenticate(PasswordReply reply)
        {
            byte[] key = Convert.FromBase64String(reply.Nonce64);
            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = _tenantSystemStorage.SingleKeyValueStorage.Get<NonceData>(new GuidId(key));

            // TODO TEST Make sure an exception is thrown if it does not exist.
            Guard.Argument(noncePackage, nameof(noncePackage)).NotNull("Invalid nonce specified");

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            _tenantSystemStorage.SingleKeyValueStorage.Delete(new GuidId(key));

            // Here we test if the client's provided nonce is saved on the server and if the
            // client's calculated nonceHash is equal to the same calculation on the server
            await _secretService.AssertPasswordKeyMatch(reply.NonceHashedPassword64, reply.Nonce64);

            //now that the password key matches, we set return the client auth token
            var keys = await this._secretService.GetOfflineRsaKeyList();
            var (clientToken, serverToken) = OwnerConsoleTokenManager.CreateToken(noncePackage, reply, keys);

            _tenantSystemStorage.SingleKeyValueStorage.Upsert(serverToken.Id, serverToken);

            // TODO - where do we set the MasterKek and MasterDek?

            // TODO: audit login some where, or in helper class below

            var token = new ClientAuthenticationToken()
            {
                Id = serverToken.Id,
                AccessTokenHalfKey = new SensitiveByteArray(clientToken.GetKey()),
                ClientTokenType = ClientTokenType.Other
            };
            
            //set the odin context so the request of this request can use the master key (note: this was added so we could set keys on first login)
            var odinContext = _httpContextAccessor.HttpContext.RequestServices.GetRequiredService<OdinContext>();
            await this.UpdateOdinContext(token, odinContext);
            await EnsureFirstOperations(token);

            return (token, serverToken.SharedSecret.ToSensitiveByteArray());
        }

        /// <summary>
        /// Determines if the <paramref name="sessionToken"/> is valid and has not expired.  
        /// </summary>
        /// <param name="sessionToken">The token to be validated</param>
        /// <returns></returns>
        public async Task<bool> IsValidToken(Guid sessionToken)
        {
            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            var entry = _tenantSystemStorage.SingleKeyValueStorage.Get<OwnerConsoleToken>(sessionToken);
            return await Task.FromResult(IsAuthTokenEntryValid(entry));
        }

        /// <summary>
        /// Returns the LoginKek used to access the primary and application data encryption keys
        /// </summary>
        public async Task<(SensitiveByteArray, SensitiveByteArray)> GetMasterKey(Guid sessionToken, SensitiveByteArray clientSecret)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = _tenantSystemStorage.SingleKeyValueStorage.Get<OwnerConsoleToken>(sessionToken);

            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new OdinClientException("Token is invalid", OdinClientErrorCode.InvalidAuthToken);
            }

            var mk = await _secretService.GetMasterKey(loginToken, clientSecret);

            //HACK: need to clone this here because the owner console token is getting wipe by the owner console token finalizer
            var len = loginToken.SharedSecret.Length;
            var clone = new byte[len];
            Buffer.BlockCopy(loginToken.SharedSecret, 0, clone, 0, len);

            loginToken.Dispose();
            return (mk, clone.ToSensitiveByteArray());
        }

        public async Task<(SensitiveByteArray masterKey, PermissionContext permissionContext)> GetPermissionContext(ClientAuthenticationToken token)
        {
            if (await IsValidToken(token.Id))
            {
                var (masterKey, clientSharedSecret) = await GetMasterKey(token.Id, token.AccessTokenHalfKey);

                var icrKey = _icrKeyService.GetMasterKeyEncryptedIcrKey();

                var allDrives = await _driveManager.GetDrives(PageOptions.All);
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
        /// Gets the <see cref="GetDotYouContext"/> for the specified token from cache or disk.
        /// </summary>
        /// <param name="token"></param>
        public Task<OdinContext> GetDotYouContext(ClientAuthenticationToken token)
        {
            var creator = new Func<Task<OdinContext>>(async delegate
            {
                var dotYouContext = new OdinContext();
                var (masterKey, permissionContext) = await GetPermissionContext(token);

                if (null == permissionContext || masterKey.IsEmpty())
                {
                    throw new OdinSecurityException("Invalid owner token");
                }

                dotYouContext.SetPermissionContext(permissionContext);

                dotYouContext.Caller = new CallerContext(
                    odinId: _tenantContext.HostOdinId,
                    masterKey: masterKey,
                    securityLevel: SecurityGroupType.Owner);

                return dotYouContext;
            });

            var ctx = _cache.GetOrAddContext(token, creator).GetAwaiter().GetResult();
            return Task.FromResult(ctx);
        }

        /// <summary>
        /// Extends the token life by <param name="ttlSeconds"></param> if it is valid.  Otherwise an <see cref="InvalidTokenException"/> is thrown
        /// </summary>
        /// <param name="token"></param>
        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            _tenantSystemStorage.SingleKeyValueStorage.Upsert(entry.Id, entry);
        }

        /// <summary>
        /// Expires the <paramref name="token"/> thus making it invalid.  This can be used when a user
        /// clicks logout.  Invalid or expired tokens are ignored.
        /// </summary>
        /// <param name="token"></param>
        public void ExpireToken(Guid token)
        {
            _tenantSystemStorage.SingleKeyValueStorage.Delete(token);
        }

        private Task<OwnerConsoleToken> GetValidatedEntry(Guid token)
        {
            var entry = _tenantSystemStorage.SingleKeyValueStorage.Get<OwnerConsoleToken>(token);
            AssertTokenIsValid(entry);
            return Task.FromResult(entry);
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
                throw new OdinSecurityException();
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

        //

        private async Task EnsureFirstOperations(ClientAuthenticationToken token)
        {
            var fli = _tenantSystemStorage.SingleKeyValueStorage.Get<FirstOwnerLoginInfo>(FirstOwnerLoginInfo.Key);
            if (fli == null)
            {
                await _tenantConfigService.CreateInitialKeys();

                _tenantSystemStorage.SingleKeyValueStorage.Upsert(FirstOwnerLoginInfo.Key, new FirstOwnerLoginInfo()
                {
                    FirstLoginDate = UnixTimeUtc.Now()
                });
            }
        }

        public async Task<bool> UpdateOdinContext(ClientAuthenticationToken token, OdinContext odinContext)
        {
            var context = _httpContextAccessor.HttpContext;
            odinContext.SetAuthContext(OwnerAuthConstants.SchemeName);

            //HACK: fix this
            //a bit of a hack here: we have to set the context as owner
            //because it's required to build the permission context
            // this is justified because we're heading down the owner api path
            // just below this, we check to see if the token was good.  if not, the call fails.
            odinContext.Caller = new CallerContext(
                odinId: (OdinId)context.Request.Host.Host,
                masterKey: null,
                securityLevel: SecurityGroupType.Owner);

            OdinContext ctx = await this.GetDotYouContext(token);

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
            var mediator = context.RequestServices.GetRequiredService<IMediator>();
            await mediator.Publish(new OwnerIsOnlineNotification() { });

            return true;
        }
    }
}