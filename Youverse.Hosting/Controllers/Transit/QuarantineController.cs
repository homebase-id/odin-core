using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Hosting.Security;

namespace Youverse.Hosting.Controllers.Transit
{
    [ApiController]
    [Route("/api/transit/quarantine")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class QuarantineController : ControllerBase
    {
        private readonly ITransitQuarantineService _svc;

        public QuarantineController(ITransitQuarantineService svc)
        {
            _svc = svc;
        }

        [HttpGet]
        public Task<JsonResult> GetQuarantinedItems(int pageNumber, int pageSize)
        {
            try
            {
                return null;
            }
            catch (InvalidDataException e)
            {
                return Task.FromResult(new JsonResult(new NoResultResponse(false, e.Message)));
            }
        }
    }
}