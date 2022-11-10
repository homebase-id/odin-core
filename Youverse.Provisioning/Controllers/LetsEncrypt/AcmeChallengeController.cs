using Microsoft.AspNetCore.Mvc;
using Youverse.Provisioning.Services.Certificate;

namespace Youverse.Provisioning.Controllers.LetsEncrypt
{
    [Route(".well-known")]
    [ApiController]
    public class AcmeChallengeController : ControllerBase
    {
        private readonly ProvisioningConfig _config;

        public AcmeChallengeController(ProvisioningConfig config)
        {
            _config = config;
        }

        [HttpGet("acme-challenge/{token}")]
        public IActionResult GetTokenContent(string token)
        {
            Console.WriteLine($"Retrieving keyAuth for token: [{token}]");
            
            //Note: file cleanup happens in the DotYouRegistry.WebHost since it knows when the certificate has been created
            var certificateAuth = CertificateAuthFile.Read(_config.CertificateChallengeTokenPath, token);
            
            if (null == certificateAuth || (string.IsNullOrEmpty(certificateAuth.Auth) || string.IsNullOrWhiteSpace(certificateAuth.Auth)))
            {
                Console.WriteLine($"Token not found or file empty");
                return NotFound();
            }
            
            Console.WriteLine($"Found token.  Returning KeyAuth");
            return Ok(certificateAuth.Auth);
        }
    }
}