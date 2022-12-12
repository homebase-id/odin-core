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
    public class CertificateManagementController : ControllerBase
    {
        private readonly ILogger<CertificateManagementController> _logger;
        private readonly ITenantCertificateRenewalService _tenantCertificateRenewalService;
        private readonly ITenantCertificateService _tenantCertificateService;

        public CertificateManagementController(ILogger<CertificateManagementController> logger, ITenantCertificateRenewalService tenantCertificateRenewalService,
            ITenantCertificateService tenantCertificateService)
        {
            _logger = logger;
            _tenantCertificateRenewalService = tenantCertificateRenewalService;
            _tenantCertificateService = tenantCertificateService;
        }
        
        
        [HttpPost("ensurevalidcertificate")]
        public async Task<IActionResult> EnsureCertificatesAreValid()
        {
            await _tenantCertificateRenewalService.EnsureCertificatesAreValid();
            return Ok();
        }

        [HttpPost("generatecertificate")]
        public async Task<CertificateOrderStatus> GenerateCertificateIfReady()
        {
            var status = await _tenantCertificateRenewalService.GenerateCertificateIfReady();
            return status;
        }

        [HttpGet("verifyCertificatesValid")]
        public async Task<IActionResult> VerifyCertifcatesValid()
        {
            bool certsAreValid =  await _tenantCertificateService.AreAllCertificatesValid();
            return new JsonResult(certsAreValid);
        }
    }
}