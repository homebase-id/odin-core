using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Security;

namespace Youverse.Hosting.Controllers.Transit
{
    [ApiController]
    [Route("/api/transit/inbox")]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner, AuthenticationSchemes = DotYouAuthConstants.DotIdentityOwnerScheme)]
    public class InboxController : ControllerBase
    {
        private readonly ITransitService _svc;

        public InboxController(ITransitService svc)
        {
            _svc = svc;
        }

        [HttpGet]
        public Task<JsonResult> GetUnprocessedItems(int pageNumber, int pageSize)
        {
            try
            {
                return null;
            }
            catch (InvalidDataException e)
            {
                return Task.FromResult<JsonResult>(new JsonResult(new NoResultResponse(false, e.Message)));
            }
        }
    }
}