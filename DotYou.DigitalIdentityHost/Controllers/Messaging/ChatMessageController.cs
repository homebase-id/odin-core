using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Messaging.Chat;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.DigitalIdentityHost.Controllers.Messaging
{

    [Route("api/messages/chat")]
    [ApiController]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class ChatMessageController : ControllerBase
    {
        readonly IChatService _chatService;
        public ChatMessageController(IChatService chatService)
        {
            _chatService = chatService;
        }
        
        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] ChatMessageEnvelope message)
        {
            await _chatService.SendMessage(message);
            return new JsonResult(new NoResultResponse(true));
        }

        [HttpGet("availablecontacts")]
        public async Task<IActionResult> GetAvailableContacts()
        {
            var contacts = await _chatService.GetAvailableContacts();
            return new JsonResult(contacts);
        }
        
    }
}
