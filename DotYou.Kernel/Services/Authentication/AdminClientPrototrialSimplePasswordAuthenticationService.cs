using System;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
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
        public AdminClientPrototrialSimplePasswordAuthenticationService(DotYouContext context, ILogger<AdminClientPrototrialSimplePasswordAuthenticationService> logger) : base(context, logger)
        {
        }
        
        public Task<Guid> Authenticate(string password, int ttlSeconds)
        {
            
            //TODO: Handle multiple clients
            
            AssertValidPassword(password);

            var entry = new AuthTokenEntry()
            {
                Id = Guid.NewGuid(),
                ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds
            };
            
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s=>s.Save(entry));

            return Task.FromResult(entry.Id);
        }

        public async Task<bool> IsValidToken(Guid token)
        {
            var entry = await WithTenantStorageReturnSingle<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
            return IsTokenValid(entry);
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

        private async Task<AuthTokenEntry> GetValidatedEntry(Guid token)
        {
            var entry = await WithTenantStorageReturnSingle<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s => s.Get(token));
            AssertTokenIsValid(entry);
            return entry;
        }
        
        private void AssertValidPassword(string password)
        {
            //no-op
            
            //throw exception if no valid password
        }

        private bool IsTokenValid(AuthTokenEntry entry)
        {
            if (null == entry)
            {
                return false;
            }
            
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var result = 
                entry.Id != Guid.Empty &&
                entry.ExpiryUnixTime > now;

            return result;
        }
        private void AssertTokenIsValid(AuthTokenEntry entry)
        {
            if(IsTokenValid(entry) ==false)
            {
                throw new InvalidAuthTokenException();
            }
        }
    }
}