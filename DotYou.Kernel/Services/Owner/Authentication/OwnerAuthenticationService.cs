using System;
using System.Linq;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Storage;
using DotYou.Types;
using DotYou.Types.Cryptography;
using Microsoft.Extensions.Logging;

/// <summary>
/// Goals here are that:
///   * the password never leaves the clients.
///   * the password hash changes with every login request, making playback impossible
///   * the private encryption key on the server is encrypted with a KEK
///   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
///   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
/// </summary>
/// 
namespace DotYou.Kernel.Services.Owner.Authentication
{
    /// <summary>
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService : DotYouServiceBase, IOwnerAuthenticationService
    {
        private readonly IOwnerSecretService _secretService;
        private readonly LiteDBSingleCollectionStorage<LoginTokenData> _tokenStorage;
        private const string AUTH_TOKEN_COLLECTION = "tko";

        public OwnerAuthenticationService(DotYouContext context, ILogger logger, IOwnerSecretService secretService) : base(context, logger, null, null)
        {
            _secretService = secretService;
        }

        public async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();

            var rsa = await _secretService.GetRsaKeyList();

            var key = RsaKeyListManagement.GetCurrentKey(rsa);
            var publicKey = RsaKeyManagement.publicPem(key);

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKey);

            WithTenantStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Save(nonce));
            return nonce;
        }

        public async Task<AuthenticationResult> Authenticate(AuthenticationNonceReply reply)
        {
            
            Guid key = new Guid(Convert.FromBase64String(reply.Nonce64));

            var noncePackage = await WithTenantStorageReturnSingle<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Get(key));

            var rp = new PasswordReply();
            rp.Nonce64 = reply.Nonce64;
            rp.NonceHashedPassword64 = reply.NonceHashedPassword64;
            rp.RsaEncrypted = reply.RsaEncrypted;
            rp.crc = reply.crc;

            var keys = await this._secretService.GetRsaKeyList();
            var (kek, sharedSecret) = LoginKeyManager.Authenticate(noncePackage, rp, keys);

            // TODO: audit login some where, or in helper class below

            var (halfCookie, loginToken) = LoginTokenManager.CreateLoginToken(kek, sharedSecret);

            WithTenantStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(loginToken));

            // Is this necessary ? :-) 
            // It would be nicer to see the cookie set here...
            return new AuthenticationResult()
            {
                Token = loginToken.Id,
                Token2 = new Guid(halfCookie)
            };
        }

        public async Task<DeviceAuthenticationResult> AuthenticateDevice(AuthenticationNonceReply reply)
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

        public async Task<bool> IsValidToken(Guid token)
        {
            var entry = await WithTenantStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
            return IsAuthTokenEntryValid(entry);
        }

        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            WithTenantStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(entry));
        }

        public void ExpireToken(Guid token)
        {
            WithTenantStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Delete(token));
        }

        public async Task<bool> IsLoggedIn()
        {
            //check if an active token exists
            var authTokens = await WithTenantStorageReturnList<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.GetList(PageOptions.Default));

            var activeToken = authTokens.Results.FirstOrDefault(IsAuthTokenEntryValid);

            return activeToken != null;
        }

        private async Task<LoginTokenData> GetValidatedEntry(Guid token)
        {
            var entry = await WithTenantStorageReturnSingle<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
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