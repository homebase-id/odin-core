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

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentMessages(int pageNumber, int pageSize)
        {
            var history = await _chatService.GetRecentMessages(new PageOptions(pageNumber, pageSize));
            return new JsonResult(history);
        }
        
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery]string dotYouId, Int64 startDateTimeOffsetSeconds, Int64 endDateTimeOffsetSeconds, int pageNumber, int pageSize)
        {
            var history = await _chatService.GetHistory((DotYouIdentity)dotYouId, startDateTimeOffsetSeconds, endDateTimeOffsetSeconds, new PageOptions(pageNumber, pageSize));

            return new JsonResult(history);
        }

        [HttpGet("availablecontacts")]
        public async Task<IActionResult> GetAvailableContacts(int pageNumber, int pageSize)
        {
            var contacts = await _chatService.GetAvailableContacts(new PageOptions(pageNumber, pageSize));
            return new JsonResult(contacts);
        }
        
    }
}
