using Microsoft.AspNetCore.Mvc;
using Serilog;
using Youverse.Core.Services.Certificate.Renewal;

namespace Youverse.Hosting.Controllers.LetsEncrypt
{
    [Route(".well-known")]
    [ApiController]
    public class AcmeChallengeController : ControllerBase
    {
        private readonly ITenantCertificateRenewalService _certificateRenewalService;


        public AcmeChallengeController(ITenantCertificateRenewalService certificateRenewalService)
        {
            // HttpContext.RequestServices
            _certificateRenewalService = certificateRenewalService;
        }

        [HttpGet("acme-challenge/{token}")]
        public IActionResult GetTokenContent(string token)
        {
            Log.Information($"Call made to acme-challenge with token [{token}]");
            string authResponse = _certificateRenewalService.GetAuthResponse(token);
            return (null == authResponse) ? NotFound() : Ok(authResponse);
        }
    }
}