using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit.Quarantine;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner.Transit
{
    [ApiController]
    [Route("/api/transit/quarantine")]
    [Authorize(Policy = OwnerPolicies.IsDigitalIdentityOwnerPolicyName, AuthenticationSchemes = OwnerAuthConstants.DotIdentityOwnerScheme)]
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