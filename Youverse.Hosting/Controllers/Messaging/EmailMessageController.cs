using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Youverse.Core;

namespace DotYou.DigitalIdentityHost.Controllers.Messaging
{
    /// <summary>
    /// Retrieves messages for the Inbox.  Also acts to accept messages 
    /// being sent to the tenant
    /// </summary>
    [Route("api/messages")]
    [ApiController]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class EmailMessageController : ControllerBase
    {
        readonly IMessagingService _messagingService;
        public EmailMessageController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpGet("folder")]
        public async Task<PagedResult<Message>> GetList([FromQuery]string folder, int pageNumber, int pageSize)
        {
            var result = await _messagingService.Mailbox.GetList(folder, new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("/{id}")]
        public async Task<Message> Get(Guid id)
        {
            var message = await _messagingService.Mailbox.Get(id);
            return message;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] Message message)
        {
            await _messagingService.SendMessage(message);
            return new JsonResult(new NoResultResponse(true));
        }
        
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _messagingService.Mailbox.Delete(id);
            return new JsonResult(new NoResultResponse(true));
        }
        
        [HttpPost]
        public async Task<IActionResult> Save([FromBody] Message message)
        {
            await _messagingService.Mailbox.Save(message);
            return new JsonResult(new NoResultResponse(true));
        }
        
    }
}
