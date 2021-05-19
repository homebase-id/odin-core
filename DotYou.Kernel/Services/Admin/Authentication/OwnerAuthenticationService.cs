using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.Types;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Admin.Authentication
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
    public class OwnerAuthenticationService : DotYouServiceBase, IOwnerAuthenticationService
    {
        private const string AUTH_TOKEN_COLLECTION = "AuthToken";

        public OwnerAuthenticationService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }
        
        public async Task<NoncePackage> GenerateNonce()
        {
            //TODO: this will be pulled from the storage
            IdentityKeySecurity sec = new IdentityKeySecurity();
            sec.SetRawPassword("p");
            
            var nonce = new NoncePackage(sec.SaltPassword, sec.SaltKek);

            //store nonce
            WithTenantStorage<NoncePackage>(AUTH_TOKEN_COLLECTION, s=>s.Save(nonce));
            
            return nonce;
        }
        
        [Obsolete]
        public Task<AuthenticationResult> Authenticate(string password, int ttlSeconds)
        {
            //TODO: Handle multiple clients (i.e. phone, other browser instances, etc.)
            
            AssertValidPassword(password);

            var entry = new AuthTokenEntry()
            {
                Id = Guid.NewGuid(),
                ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds
            };
            
            WithTenantStorage<AuthTokenEntry>(AUTH_TOKEN_COLLECTION, s=>s.Save(entry));

            return Task.FromResult(new AuthenticationResult()
            {
                Token = entry.Id,
                DotYouId =  this.Context.DotYouId
            });
        }
        
        public async Task<AuthenticationResult> Authenticate(NonceReplyPackage replyPackage)
        {

            Guid key = new Guid(Convert.FromBase64String(replyPackage.Nonce64));
            var noncePackage = await WithTenantStorageReturnSingle<NoncePackage>(AUTH_TOKEN_COLLECTION, s=>s.Get(key));
            var noncePasswordBytes = Convert.FromBase64String(replyPackage.NonceHashedPassword);

            //TODO: to close the loop - get real hashed password from database
            string hashedPasswordFromDb = "";
            
            var nonceHashedPassword = KeyDerivation.Pbkdf2(
                hashedPasswordFromDb, 
                Convert.FromBase64String(noncePackage.Nonce64), 
                KeyDerivationPrf.HMACSHA512, 
                IdentityKeySecurity.ITERATIONS, 
                IdentityKeySecurity.HASH_SIZE);

            bool match = YFByteArray.EquiByteArrayCompare(noncePasswordBytes, nonceHashedPassword);

            if (match == false)
            {
                throw new AuthenticationException();
            }
            
            //TODO: audit login somehwere
            
            return new AuthenticationResult()
            {
                Token = Guid.NewGuid(), //TODO: update this class to include the client part of the key
                DotYouId =  this.Context.DotYouId
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