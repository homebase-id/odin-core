using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;
using Youverse.Core.Services;
using Youverse.Services.Messaging;
using Youverse.Services.Messaging.Email;

namespace Youverse.Hosting.Controllers.Perimeter
{
    [ApiController]
    [Route("api/perimeter")]
    public class EmailMessageController : ControllerBase
    {
        IMessagingService _messagingService;
        public EmailMessageController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpPost("email")]
        public async Task<IActionResult> ReceiveIncomingEmailMessage([FromBody] Message message)
        {
            _messagingService.RouteIncomingMessage(message);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}
