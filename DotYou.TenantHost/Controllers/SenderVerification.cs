
using DotYou.Kernel.Services.Verification;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers
{
    [Route("api/verify")]
    [ApiController]
    public class SenderVerification : Controller
    {
        ISenderVerificationService _verificationSvc;
        public SenderVerification(ISenderVerificationService verificationSvc)
        {
            _verificationSvc = verificationSvc;
        }

        [HttpPost]
        public void Post([FromBody] VerificationPackage package)
        {
            _verificationSvc.AssertValidToken(package);
        }

        [HttpGet]
        public string Get()
        {
            
            return User.Identity.Name;
        }

    }
}
