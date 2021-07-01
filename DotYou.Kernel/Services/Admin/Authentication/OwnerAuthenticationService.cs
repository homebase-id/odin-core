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
            var nonce = new NoncePackage(salts.SaltPassword64, salts.SaltKek64);
            WithTenantStorage<NoncePackage>(AUTH_TOKEN_COLLECTION, s => s.Save(nonce));
            return nonce;
        }
        
        public async Task<AuthenticationResult> Authenticate(AuthenticationNonceReply reply)
        {

            bool match = Guid.Parse(reply.Nonce64) == Guid.Parse("9cc5adc2-4f8a-419a-b340-8d69cba6c462");
            if (match == false)
            {
                throw new AuthenticationException();
            }

            Guid token = Guid.Parse("9cc5adc2-4f8a-419a-b340-8d69cba6c462");
            Guid clientHalf = Guid.Parse("9cc5adc2-4f8a-419a-b340-8d69cba6c462");
            
            //HACK - i created this branch so i could continue
            //working on the rn client before integrating web crypto stuff
            return new AuthenticationResult()
            {
                Token = token,
                Token2 = clientHalf,
                DotYouId = this.Context.DotYouId
            };

        }
        
        public async Task<bool> IsValidToken(Guid token)
        {
            bool valid = token == Guid.Parse("9cc5adc2-4f8a-419a-b340-8d69cba6c462");
            
            //HACK - i created this branch so i could continue
            //working on the rn client before integrating web crypto stuff
            return await Task.FromResult(valid);
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