using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Hosting.Authentication.System;
using Youverse.Hosting.Controllers.OwnerToken;

namespace Youverse.Hosting.Controllers.System
{
    /// <summary>
    /// Controller to enable kickoff of background tasks.  By running this over http, we keep the multi-tenant pattern working
    /// </summary>
    [ApiController]
    [Route(OwnerApiPathConstants.ConfigurationV1 + "/certificate")]
    [Authorize(Policy = SystemPolicies.IsSystemProcess, AuthenticationSchemes = SystemAuthConstants.SchemeName)]
    public class IdentitySetupController : ControllerBase
    {
        private readonly ILogger<IdentitySetupController> _logger;
        private readonly ITenantCertificateRenewalService _tenantCertificateRenewalService;
        private readonly ITenantCertificateService _tenantCertificateService;

        public IdentitySetupController(ILogger<IdentitySetupController> logger, ITenantCertificateRenewalService tenantCertificateRenewalService,
            ITenantCertificateService tenantCertificateService)
        {
            _logger = logger;
            _tenantCertificateRenewalService = tenantCertificateRenewalService;
            _tenantCertificateService = tenantCertificateService;
        }

        [HttpPost("initializecertificate")]
        public async Task<IActionResult> InitializeCertificate()
        {
            _logger.LogInformation("Initialize Certificate called");
            await _tenantCertificateRenewalService.EnsureCertificatesAreValid(true);
            return Ok();
        }
        
    }
}