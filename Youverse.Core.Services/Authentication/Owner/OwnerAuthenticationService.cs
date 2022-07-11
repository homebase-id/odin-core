using System;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.SystemStorage;

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
    public class OwnerAuthenticationService : IOwnerAuthenticationService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IOwnerSecretService _secretService;
        private const string AUTH_TOKEN_COLLECTION = "tko";

        public OwnerAuthenticationService(DotYouContextAccessor contextAccessor, ILogger<IOwnerAuthenticationService> logger, IOwnerSecretService secretService, ISystemStorage systemStorage)
        {
            _secretService = secretService;
            _systemStorage = systemStorage;
        }

        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();

            var rsa = await _secretService.GetRsaKeyList();

            var key = RsaKeyListManagement.GetCurrentKey(ref RsaKeyListManagement.zeroSensitiveKey, ref rsa, out var keyListWasUpdated); // TODO

            if (keyListWasUpdated)
            {
            }

            var publicKey = key.publicPem();

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKey, key.crc32c);
            _systemStorage.KeyValueStorage.Upsert(nonce.Id.ToByteArray(), nonce);
            return nonce;
        }

        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> Authenticate(IPasswordReply reply)
        {
            byte[] key = Convert.FromBase64String(reply.Nonce64);
            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = _systemStorage.KeyValueStorage.Get<NonceData>(key);

            // TODO TEST Make sure an exception is thrown if it does not exist. 
            Guard.Argument(noncePackage, nameof(noncePackage)).NotNull("Invalid nonce specified");

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            _systemStorage.KeyValueStorage.Delete(key);

            // Here we test if the client's provided nonce is saved on the server and if the
            // client's calculated nonceHash is equal to the same calculation on the server
            await _secretService.TryPasswordKeyMatch(reply.NonceHashedPassword64, reply.Nonce64);

            var keys = await this._secretService.GetRsaKeyList();
            var (clientToken, serverToken) = OwnerConsoleTokenManager.CreateToken(noncePackage, reply, keys);
            
            _systemStorage.KeyValueStorage.Upsert(serverToken.Id.ToByteArray(), serverToken);
            
            // TODO - where do we set the MasterKek and MasterDek?

            // TODO: audit login some where, or in helper class below


            var auth = new ClientAuthenticationToken()
            {
                Id = serverToken.Id,
                AccessTokenHalfKey = new SensitiveByteArray(clientToken.GetKey())
            };

            return (auth, serverToken.SharedSecret.ToSensitiveByteArray());
        }

        public async Task<bool> IsValidToken(Guid sessionToken)
        {
            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            var entry = _systemStorage.KeyValueStorage.Get<OwnerConsoleToken>(sessionToken.ToByteArray());
            return IsAuthTokenEntryValid(entry);
        }

        public async Task<(SensitiveByteArray, SensitiveByteArray)> GetMasterKey(Guid sessionToken, SensitiveByteArray clientSecret)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = _systemStorage.KeyValueStorage.Get<OwnerConsoleToken>(sessionToken.ToByteArray());

            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new Exception("Token is invalid");
            }

            var mk = await _secretService.GetMasterKey(loginToken, clientSecret);

            //HACK: need to clone this here because the owner console token is getting wipe by the owner console token finalizer
            var len = loginToken.SharedSecret.Length;
            var clone = new byte[len];
            Buffer.BlockCopy(loginToken.SharedSecret, 0, clone, 0, len);

            loginToken.Dispose();
            return (mk, clone.ToSensitiveByteArray());
        }

        // public async Task<SensitiveByteArray> GetMasterKey(Guid sessionToken, SensitiveByteArray clientSecret)
        // {
        //     //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
        //     var loginToken = await _systemStorage.WithTenantSystemStorageReturnSingle<OwnerConsoleToken>(AUTH_TOKEN_COLLECTION, s => s.Get(sessionToken));
        //     if (!IsAuthTokenEntryValid(loginToken))
        //     {
        //         throw new Exception("Token is invalid");
        //     }
        //
        //     return await _secretService.GetMasterKey(loginToken, clientSecret);
        // }

        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            _systemStorage.KeyValueStorage.Upsert(entry.Id.ToByteArray(), entry);
        }

        public void ExpireToken(Guid token)
        {
            _systemStorage.KeyValueStorage.Delete(token.ToByteArray());
        }

        private Task<OwnerConsoleToken> GetValidatedEntry(Guid token)
        {
            var entry = _systemStorage.KeyValueStorage.Get<OwnerConsoleToken>(token.ToByteArray());
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
    }
}