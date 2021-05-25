using System.Threading.Tasks;
using DotYou.Kernel.Services.Messaging.Chat;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Perimeter
{
    [ApiController]
    [Route("api/perimeter")]
    public class ChatMessageController : ControllerBase
    {
        private IChatService _chatService;
        public ChatMessageController(IChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> ReceiveIncomingChatMessage([FromBody] ChatMessageEnvelope message)
        {
            await _chatService.ReceiveIncomingMessage(message);
            return new JsonResult(new NoResultResponse(true));
        }
    }
}
