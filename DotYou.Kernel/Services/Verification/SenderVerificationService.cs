using DotYou.Types;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Verification
{
    public class SenderVerificationService : ISenderVerificationService
    {
        IMemoryCache _cache;
        ILogger<SenderVerificationService> _logger;
        IDotYouHttpClientProxy _httpProxy;

        //ILogger<SenderVerificationService> logger
        public SenderVerificationService(IMemoryCache memoryCache, IDotYouHttpClientProxy httpProxy)
        {
            _cache = memoryCache;
            _httpProxy = httpProxy;
        //    _logger = logger;
        }

        public void AddVerifiable(IVerifiable verifiable, int ttlSeconds = 10)
        {
            Guid token = verifiable.GetToken();
            string checksum = verifiable.GetChecksum();
            _cache.Set(token, checksum, TimeSpan.FromSeconds(ttlSeconds));
        }

        public async Task AssertTokenVerified(DotYouIdentity dotYouId, IVerifiable verifiable)
        {

            var package = new VerificationPackage()
            {
                Token = verifiable.GetToken(),
                Checksum = verifiable.GetChecksum()
            };

            bool success = await _httpProxy.Post<VerificationPackage>(dotYouId, "/api/verify", package);

            if (success)
            {
                _logger.LogInformation($"Token Verfication Success: [{verifiable.GetToken()}]");
            }
            else
            {
                var ex = new VerificationFailedException();
                _logger.LogError(ex, $"Failed Verifying Token [{verifiable.GetToken()}]");
                throw ex;
            }

        }

        public void AssertValidToken(VerificationPackage package)
        {
            string value;

            if (_cache.TryGetValue(package.Token, out value))
            {
                if (value == package.Checksum)
                {
                    return;
                }
            }

            throw new VerificationFailedException();
        }

    }
}
