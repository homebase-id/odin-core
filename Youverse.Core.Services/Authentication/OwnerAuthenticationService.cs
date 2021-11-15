using System;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
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
namespace Youverse.Core.Services.Authentication
{
    /// <summary>
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService : DotYouServiceBase, IOwnerAuthenticationService
    {
        private readonly IOwnerSecretService _secretService;
        // private readonly LiteDBSingleCollectionStorage<LoginTokenData> _tokenStorage;
        private const string AUTH_TOKEN_COLLECTION = "tko";

        public OwnerAuthenticationService(DotYouContext context, ILogger logger, IOwnerSecretService secretService) : base(context, logger, null, null)
        {
            _secretService = secretService;
        }

        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();

            var rsa = await _secretService.GetRsaKeyList();

            var key = RsaKeyListManagement.GetCurrentKey(ref rsa, out var keyListWasUpdated);

            if (keyListWasUpdated)
            {
                
            }
            
            var publicKey = RsaKeyManagement.publicPem(key);

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKey, key.crc32c);

            WithTenantSystemStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Save(nonce));
            return nonce;
        }

        public async Task<DotYouAuthenticationResult> Authenticate(IPasswordReply reply)
        {
            
            Guid key = new Guid(Convert.FromBase64String(reply.Nonce64));

            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = await WithTenantSystemStorageReturnSingle<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Get(key));

            // TODO TEST Make sure an exception is thrown if it does not exist. 
            Guard.Argument(noncePackage, nameof(noncePackage)).NotNull("Invalid nonce specified");

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            WithTenantSystemStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Delete(key));

            // Here we test if the client's provided nonce is saved on the server and if the
            // client's calculated nonceHash is equal to the same calculation on the server
            await _secretService.TryPasswordKeyMatch(reply.NonceHashedPassword64, reply.Nonce64);

            var keys = await this._secretService.GetRsaKeyList();
            var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(noncePackage, reply, keys);
            
            WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(loginToken));

            // TODO - where do we set the MasterKek and MasterDek?

            // TODO: audit login some where, or in helper class below

         
            return new DotYouAuthenticationResult()
            {
                SessionToken = loginToken.Id,
                ClientHalfKek = new SecureKey(halfCookie)
            };
        }


        public async Task<DeviceAuthenticationResult> AuthenticateDevice(IPasswordReply reply)
        {
            var authResult = await Authenticate(reply);

            //TODO: extra device auth stuff here - like seeing if it's an authorized device, etc.
            //HACK: hard coded until we integrate michael's stuff
            var deviceToken = Guid.Parse("9cc5adc2-4f8a-419a-b340-8d69cba6c462");

            var result = new DeviceAuthenticationResult()
            {
                AuthenticationResult = authResult,
                DeviceToken = deviceToken
            };

            return result;
        }

        public async Task<bool> IsValidDeviceToken(Guid token)
        {
            //HACK
            if (token == Guid.Parse("9cc5adc2-4f8a-419a-b340-8d69cba6c462"))
            {
                return true;
            }

            return false;
        }

        public async Task<bool> IsValidToken(Guid sessionToken)
        {
            var entry = await WithTenantSystemStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(sessionToken));
            return IsAuthTokenEntryValid(entry);
        }
        
        public async Task<SecureKey> GetLoginDek(Guid sessionToken, SecureKey clientHalfKek)
        {
            //TODO: need to audit who and what and why this was accessed (add justification/reason on parameters)
            var loginToken = await WithTenantSystemStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(sessionToken));
            if (!IsAuthTokenEntryValid(loginToken))
            {
                throw new Exception("Token is invalid");
            }

            var encryptedDek  = await _secretService.GetEncryptedDek();
            var loginKek = LoginTokenManager.GetLoginKek(loginToken.HalfKey, clientHalfKek.GetKey());
            
            var dek = LoginKeyManager.GetDek(encryptedDek.GetKey(), loginKek.GetKey());
           
            encryptedDek.Wipe();
            loginKek.Wipe();
            loginToken.Dispose();
            
            return new SecureKey(dek);
        }


        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(entry));
        }

        public void ExpireToken(Guid token)
        {
            WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Delete(token));
        }

        public async Task<bool> IsLoggedIn()
        {
            //check if an active token exists
            var authTokens = await WithTenantSystemStorageReturnList<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.GetList(PageOptions.Default));

            var activeToken = authTokens.Results.FirstOrDefault(IsAuthTokenEntryValid);

            return activeToken != null;
        }

        private async Task<LoginTokenData> GetValidatedEntry(Guid token)
        {
            var entry = await WithTenantSystemStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
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
                throw new AuthenticationException();
            }
        }

       
    }
}