using System;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authentication
{
    public class MacHackAuthenticationService : OwnerAuthenticationService
    {
        
        public MacHackAuthenticationService(DotYouContext context, ILogger<MacHackAuthenticationService> logger, IOwnerSecretService secretService, ISystemStorage systemStorage) : base(context, logger, secretService, systemStorage)
        {
            
        }
        
        public override async Task<NonceData> GenerateAuthenticationNonce()
        {
            var salts = await _secretService.GetStoredSalts();
            
            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, "publicKey", 0);

            _systemStorage.WithTenantSystemStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Save(nonce));
            return nonce;
        }

        public override async Task<DotYouAuthenticationResult> Authenticate(IPasswordReply reply)
        {
            Guid key = new Guid(Convert.FromBase64String(reply.Nonce64));

            // Ensure that the Nonce given by the client can be loaded, throw exception otherwise
            var noncePackage = await _systemStorage.WithTenantSystemStorageReturnSingle<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Get(key));

            // TODO TEST Make sure an exception is thrown if it does not exist. 
            Guard.Argument(noncePackage, nameof(noncePackage)).NotNull("Invalid nonce specified");

            // TODO TEST Make sure the nonce saved is deleted and can't be replayed.
            _systemStorage.WithTenantSystemStorage<NonceData>(AUTH_TOKEN_COLLECTION, s => s.Delete(key));
            
            byte[] halfCookie = Guid.Empty.ToByteArray();
            var loginToken = new LoginTokenData()
            {
                Id = Guid.NewGuid(),
                HalfKey = Guid.Empty.ToByteArray(),
                SharedSecret = Guid.Empty.ToByteArray(),
                ExpiryUnixTime = DateTimeOffset.UtcNow.AddDays(101).ToUnixTimeMilliseconds(),
                
            };
            
            _systemStorage.WithTenantSystemStorage<LoginTokenData>(AUTH_TOKEN_COLLECTION, s => s.Save(loginToken));

            return new DotYouAuthenticationResult()
            {
                SessionToken = loginToken.Id,
                ClientHalfKek = new SecureKey(halfCookie)
            };
        }
    }
}