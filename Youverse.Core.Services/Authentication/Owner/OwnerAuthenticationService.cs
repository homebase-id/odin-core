using System;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;

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
    public class OwnerAuthenticationService :  IOwnerAuthenticationService
    {
        private readonly ISystemStorage _systemStorage;
        private readonly IOwnerSecretService _secretService;
        private const string AUTH_TOKEN_COLLECTION = "tko";

        public OwnerAuthenticationService(DotYouContext context, ILogger<IOwnerAuthenticationService> logger, IOwnerSecretService secretService, ISystemStorage systemStorage)
        {
            _secretService = secretService;
            _systemStorage = systemStorage;
        }

        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();

            var rsa = await _secretService.GetRsaKeyList();

            var key = RsaKeyListManagement.GetCurrentKey(ref rsa, out var keyListWasUpdated);

            if (keyListWasUpdated)
            {
                
            }
            
            var publicKey = key.publicPem();

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKey, key.crc32c);

            _systemStorage.WithTenantSystemStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Save(nonce));
            return nonce;
        }

        public async Task<DotYouAuthenticationResult> Authenticate(IPasswordReply reply)
        {
            
            Guid key = new Guid(Convert.FromBase64String(reply.Nonce64));

            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = await _systemStorage.WithTenantSystemStorageReturnSingle<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Get(key));

            // TODO TEST Make sure an exception is thrown if it does not exist. 
            Guard.Argument(noncePackage, nameof(noncePackage)).NotNull("Invalid nonce specified");

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            _systemStorage.WithTenantSystemStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Delete(key));

            // Here we test if the client's provided nonce is saved on the server and if the
            // client's calculated nonceHash is equal to the same calculation on the server
            await _secretService.TryPasswordKeyMatch(reply.NonceHashedPassword64, reply.Nonce64);

            var keys = await this._secretService.GetRsaKeyList();
            var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(noncePackage, reply, keys);
            
            _systemStorage.WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(loginToken));

            // TODO - where do we set the MasterKek and MasterDek?

            // TODO: audit login some where, or in helper class below

         
            return new DotYouAuthenticationResult()
            {
                SessionToken = loginToken.Id,
                ClientHalfKek = new SecureKey(halfCookie)
            };
        }

        public async Task<bool> IsValidToken(Guid sessionToken)
        {
            
            //TODO: need to add some sort of validation that this deviceUid has not been rejected/blocked
            
            var entry = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(sessionToken));
            return IsAuthTokenEntryValid(entry);
        }
        
        public async Task<SecureKey> GetMasterKey(Guid sessionToken, SecureKey clientHalfKek)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(sessionToken));
            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new Exception("Token is invalid");
            }

            return await _secretService.GetMasterKey(loginToken, clientHalfKek);
        }

        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            _systemStorage.WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(entry));
        }

        public void ExpireToken(Guid token)
        {
            _systemStorage.WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Delete(token));
        }

        private async Task<LoginTokenData> GetValidatedEntry(Guid token)
        {
            var entry = await _systemStorage.WithTenantSystemStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
            AssertTokenIsValid(entry);
            return entry;
        }

        private bool IsAuthTokenEntryValid(LoginTokenData entry)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var valid =
                null != entry &&
                entry.Id != Guid.Empty &&
                entry.ExpiryUnixTime > now;

            return valid;
        }

        private void AssertTokenIsValid(LoginTokenData entry)
        {
            if (IsAuthTokenEntryValid(entry) == false)
            {
                throw new YouverseSecurityException();
            }
        }

       
    }
}