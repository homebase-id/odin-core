using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Kernel.HttpClient;
using DotYou.Types;
using DotYou.Types.Cryptography;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Constants = Microsoft.VisualBasic.Constants;

/// <summary>
/// Goals here are that:
///   * the password never leaves the clients.
///   * the password hash changes with every login request, making playback impossible
///   * the private encryption key on the server is encrypted with a KEK
///   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
///   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
/// </summary>
/// 
namespace DotYou.Kernel.Services.Admin.Authentication
{
    /// <summary>
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class OwnerAuthenticationService : DotYouServiceBase, IOwnerAuthenticationService
    {
        private readonly IOwnerSecretService _secretService;
        private const string AUTH_TOKEN_COLLECTION = "tko";

        public OwnerAuthenticationService(DotYouContext context, ILogger logger, IOwnerSecretService secretService) : base(context, logger, null, null)
        {
            _secretService = secretService;
        }

        public async Task<NoncePackage> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();
            var nonce = new NoncePackage(salts.SaltPassword64, salts.SaltKek64, "xxx");
            WithTenantStorage<NoncePackage>(AUTH_TOKEN_COLLECTION, s => s.Save(nonce));
            return nonce;
        }
        
        public async Task<AuthenticationResult> Authenticate(AuthenticationNonceReply reply)
        {
            Guid key = new Guid(Convert.FromBase64String(reply.Nonce64));

            var noncePackage = await WithTenantStorageReturnSingle<NoncePackage>(AUTH_TOKEN_COLLECTION, s => s.Get(key));

            var match = await _secretService.IsPasswordKeyMatch(reply.NonceHashedPassword64, noncePackage.Nonce64);
            
            if (match == false)
            {
                throw new AuthenticationException();
            }

            //TODO: audit login some where

            const int ttlSeconds = 60 * 10;
            
            var kekBytes = Convert.FromBase64String(reply.KeK64);
            byte[] serverHalf = YFByteArray.GetRndByteArray(16);
            byte[] clientHalf = YFByteArray.EquiByteArrayXor(kekBytes, serverHalf);

            var token = YFByteArray.GetRandomCryptoGuid();
            var entry = new AuthTokenEntry()
            {
                Id = token,
                KekKey = new Guid(serverHalf),
                ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds
            };

            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Save(entry));

            return new AuthenticationResult()
            {
                Token = token,
                Token2 = new Guid(clientHalf),
                DotYouId = this.Context.DotYouId
            };
        }
        
        public async Task<bool> IsValidToken(Guid token)
        {
            var entry = await WithTenantStorageReturnSingle<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
            return IsAuthTokenEntryValid(entry);
        }

        public async Task ExtendTokenLife(Guid token, int ttlSeconds)
        {
            var entry = await GetValidatedEntry(token);

            entry.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds;

            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Save(entry));
        }

        public void ExpireToken(Guid token)
        {
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Delete(token));
        }
        
        public async Task<bool> IsLoggedIn()
        {
            //check if an active token exists
            var authTokens = await WithTenantStorageReturnList<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.GetList(PageOptions.Default));
            
            var activeToken = authTokens.Results.FirstOrDefault(IsAuthTokenEntryValid);

            return activeToken != null;
        }

        private async Task<AuthTokenEntry> GetValidatedEntry(Guid token)
        {
            var entry = await WithTenantStorageReturnSingle<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
            AssertTokenIsValid(entry);
            return entry;
        }
        
        private bool IsAuthTokenEntryValid(AuthTokenEntry entry)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var valid =
                null != entry &&
                entry.Id != Guid.Empty &&
                entry.ExpiryUnixTime > now;


            return valid;
        }

        private void AssertTokenIsValid(AuthTokenEntry entry)
        {
            if (IsAuthTokenEntryValid(entry) == false)
            {
                throw new AuthenticationException();
            }
        }
    }
}