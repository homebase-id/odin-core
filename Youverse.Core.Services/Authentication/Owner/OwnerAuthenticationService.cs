using System;
using System.Threading;
using System.Threading.Tasks;
using Dawn;
using MediatR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Storage;

/// <summary>
/// Goals here are that:
///   * the password never leaves the clients.
///   * the password hash changes with every login request, making playback impossible
///   * the private encryption key on the server is encrypted with a KEK
///   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
///   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
/// </summary>
///
namespace Youverse.Core.Services.Authentication.Owner
{
    /// <summary>
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService : IOwnerAuthenticationService, INotificationHandler<DriveDefinitionAddedNotification>
    {
        private readonly ITenantSystemStorage _tenantSystemStorage;
        private readonly IOwnerSecretService _secretService;

        private readonly DotYouContextCache _cache;
        private readonly ILogger<IOwnerAuthenticationService> _logger;

        public OwnerAuthenticationService(ILogger<IOwnerAuthenticationService> logger, IOwnerSecretService secretService, ITenantSystemStorage tenantSystemStorage)
        {
            _logger = logger;
            _secretService = secretService;
            _tenantSystemStorage = tenantSystemStorage;
            _cache = new DotYouContextCache();
        }

        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();
            var (publicKeyCrc32C, publicKeyPem) = await _secretService.GetCurrentAuthenticationRsaKey();

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKeyPem, publicKeyCrc32C);
            _tenantSystemStorage.SingleKeyValueStorage.Upsert(nonce.Id, nonce);
            return nonce;
        }

        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> Authenticate(IPasswordReply reply)
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
            await _secretService.TryPasswordKeyMatch(reply.NonceHashedPassword64, reply.Nonce64);

            var keys = await this._secretService.GetRsaKeyList();
            var (clientToken, serverToken) = OwnerConsoleTokenManager.CreateToken(noncePackage, reply, keys);

            _tenantSystemStorage.SingleKeyValueStorage.Upsert(serverToken.Id, serverToken);

            // TODO - where do we set the MasterKek and MasterDek?

            // TODO: audit login some where, or in helper class below


            var auth = new ClientAuthenticationToken()
            {
                Id = serverToken.Id,
                AccessTokenHalfKey = new SensitiveByteArray(clientToken.GetKey()),
                ClientTokenType = ClientTokenType.Other
            };

            return (auth, serverToken.SharedSecret.ToSensitiveByteArray());
        }

        public async Task<bool> IsValidToken(Guid sessionToken)
        {
            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            var entry = _tenantSystemStorage.SingleKeyValueStorage.Get<OwnerConsoleToken>(sessionToken);
            return IsAuthTokenEntryValid(entry);
        }

        public async Task<(SensitiveByteArray, SensitiveByteArray)> GetMasterKey(Guid sessionToken, SensitiveByteArray clientSecret)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = _tenantSystemStorage.SingleKeyValueStorage.Get<OwnerConsoleToken>(sessionToken);

            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new YouverseClientException("Token is invalid", YouverseClientErrorCode.InvalidAuthToken);
            }

            var mk = await _secretService.GetMasterKey(loginToken, clientSecret);

            //HACK: need to clone this here because the owner console token is getting wipe by the owner console token finalizer
            var len = loginToken.SharedSecret.Length;
            var clone = new byte[len];
            Buffer.BlockCopy(loginToken.SharedSecret, 0, clone, 0, len);

            loginToken.Dispose();
            return (mk, clone.ToSensitiveByteArray());
        }

        public bool TryGetCachedContext(ClientAuthenticationToken token, out DotYouContext context)
        {
            return _cache.TryGetContext(token, out context);
        }

        public void CacheContext(ClientAuthenticationToken token, DotYouContext dotYouContext)
        {
            _cache.CacheContext(token, dotYouContext);
        }

        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            _tenantSystemStorage.SingleKeyValueStorage.Upsert(entry.Id, entry);
        }

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
                throw new YouverseSecurityException();
            }
        }

        public Task Handle(DriveDefinitionAddedNotification notification, CancellationToken cancellationToken)
        {
            //reset cache so the drive is reached on the next request
            if (notification.IsNewDrive)
            {
                _logger.LogDebug("New drive created [{0}]; Purging cache ", notification.Drive.TargetDriveInfo);
                _cache.Purge();
            }

            return Task.CompletedTask;
        }
    }
}