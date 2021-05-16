using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Incoming
{
    [ApiController]
    [Route("api/incoming")]
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
            //TODO: Move this to a generic interface that sets it for all other incoming classes
            message.Received = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _messagingService.RouteIncomingMessage(message);

            return new JsonResult(new NoResultResponse(true));
        }
    }
}
