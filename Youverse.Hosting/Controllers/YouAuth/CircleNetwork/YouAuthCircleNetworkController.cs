using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Controllers.YouAuth.CircleNetwork
{
    [ApiController]
    [Route(YouAuthApiPathConstants.CircleNetwork)]
    [Authorize(AuthenticationSchemes = YouAuthConstants.Scheme)]
    public class YouAuthCircleNetworkController : ControllerBase
    {
        private readonly ICircleNetworkService _circleNetworkService;
        private readonly ICircleNetworkRequestService _circleNetworkRequestService;

        private readonly DotYouContextAccessor _contextAccessor;

        public YouAuthCircleNetworkController(DotYouContextAccessor contextAccessor, ICircleNetworkService circleNetworkService, ICircleNetworkRequestService circleNetworkRequestService)
        {
            _contextAccessor = contextAccessor;
            _circleNetworkService = circleNetworkService;
            _circleNetworkRequestService = circleNetworkRequestService;
        }

        //

        [HttpGet("connections")]
        public async Task<IActionResult> GetConnections(int pageNumber, int pageSize)
        {
            var connections = await _circleNetworkService.GetConnectedProfiles(new PageOptions(pageNumber, pageSize));
            return new JsonResult(connections);
        }
    }
}