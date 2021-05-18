using System.Threading.Tasks;
using DotYou.Kernel.Services.Messaging.Chat;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Incoming
{
    [ApiController]
    [Route("api/incoming")]
    public class ChatMessageController : ControllerBase
    {
        private IChatService _chatService;
        public ChatMessageController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> ReceiveIncomingEmailMessage([FromBody] ChatMessageEnvelope message)
        {
            await _chatService.ReceiveIncomingMessage(message);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}
