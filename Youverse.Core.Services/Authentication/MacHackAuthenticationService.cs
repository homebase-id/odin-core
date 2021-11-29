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
            
            var publicKey64 = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6Tt75Wgd7iVOlFk9sTl/+d/oiiMPNH5NtHaK6uOPE1GRCSXWbvvY46+vrgNIk3DZCDSPCk26e0U+AvB/mwtZFaqcRrg3rbO2jcGQWybYZdTA+UqQNVi1BSxRCRlFptGoM+pdGnnAG8o80VwWZlryUPiMXM2FF/BhHSOxDoMfXgFKJnxc+4Mvdzu5qYA+/ivjgCmT+zUhb00eSWnCCgnB4SXRFP/VZB2isH/ovfJ6kTGDE+e1Ct3gQD6mst0CcSe9YvXhYhADqjOO5nLIq4b+BXoM18ce4qy9t75/AmdW9PdOx7CikVDHNrhVwYAt9rNTnftW9yAPmUX9pGydoAlyqQIDAQAB";
            // var publicKey = Convert.FromBase64String(publicKey64);
            // var privateKey = Guid.Parse("0000000F-0f85-DDDD-a7eb-e8e0b06c2555").ToByteArray();

            var nonce = new NonceData(salts.SaltPassword64, salts.SaltKek64, publicKey64, 0);

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