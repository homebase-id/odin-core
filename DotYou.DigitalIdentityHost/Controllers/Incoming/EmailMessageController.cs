using System.Threading.Tasks;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Incoming
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
            _messagingService.RouteIncomingMessage(message);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}
