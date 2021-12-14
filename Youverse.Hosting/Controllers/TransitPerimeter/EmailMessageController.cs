using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services.Authorization;
using Youverse.Hosting.Authentication.TransitPerimeter;
using Youverse.Services.Messaging;
using Youverse.Services.Messaging.Email;

namespace Youverse.Hosting.Controllers.TransitPerimeter
{
    [ApiController]
    [Route("api/perimeter")]
    [Authorize(Policy = TransitPerimeterPolicies.MustBeIdentifiedPolicyName, AuthenticationSchemes = TransitPerimeterAuthConstants.TransitAuthScheme)]
    public class EmailMessageController : ControllerBase
    {
        IMessagingService _messagingService;

        public EmailMessageController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpPost("email")]
        public Task<JsonResult> ReceiveIncomingEmailMessage([FromBody] Message message)
        {
            _messagingService.RouteIncomingMessage(message);
            return Task.FromResult(new JsonResult(new NoResultResponse(true)));
        }
    }
}