using System;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Services.Admin.Authentication;
using DotYou.Kernel.Services.Admin.IdentityManagement;
using DotYou.Types;
using DotYou.Types.Admin;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Authentication
{
    public class AuthTokenEntry
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Point in time the token expires
        /// </summary>
        public Int64 ExpiryUnixTime { get; set; }
    }
    
    /// <summary>
    /// Basic password authentication.  Returns a token you can use to maintain state of authentication (i.e. store in a cookie)
    /// </summary>
    public class AdminClientPrototrialSimplePasswordAuthenticationService : DotYouServiceBase, IAdminClientAuthenticationService
    {
        private const string AUTH_TOKEN_COLLECTION = "AuthToken";

        public AdminClientPrototrialSimplePasswordAuthenticationService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }
        
        public Task<AuthenticationResult> Authenticate(string password, int ttlSeconds)
        {
            //TODO: Handle multiple clients (i.e. phone, other browser instances, etc.)
            
            AssertValidPassword(password);

            var entry = new AuthTokenEntry()
            {
                Id = Guid.NewGuid(),
                ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds
            };
            
            //remove old tokens
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s=>s.DeleteAll());
            
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s=>s.Save(entry));

            return Task.FromResult(new AuthenticationResult()
            {
                Token = entry.Id,
                DotYouId =  this.Context.DotYouId
            });
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
            
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s=>s.Save(entry));
        }

        public void ExpireToken(Guid token)
        {
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s=>s.Delete(token));
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
        
        private void AssertValidPassword(string password)
        {
            //no-op for now
            
            //throw exception if no valid password
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
            if(IsAuthTokenEntryValid(entry) ==false)
            {
                throw new AuthenticationException();
            }
        }
    }
}