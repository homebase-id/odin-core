using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Apps.Transit
{
    [ApiController]
    [Route("/api/transit/audit")]
    [Authorize(Policy = AppPolicies.IsAuthorizedApp, AuthenticationSchemes = AppAuthConstants.SchemeName)]
    public class TransitAuditController : ControllerBase
    {
        private readonly ITransitAuditReaderService _audit;

        public TransitAuditController(ITransitAuditReaderService audit)
        {
            _audit = audit;
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetLast(int seconds, int pageNumber, int pageSize)
        {
            var pageOptions = new PageOptions(pageNumber, pageSize);
            var result = await _audit.GetList(TimeSpan.FromSeconds(seconds), pageOptions);
            return new JsonResult(result);
        }
    }
}