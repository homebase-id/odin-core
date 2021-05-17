using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Messaging.Email
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

        [HttpGet("folder/{folder}")]
        public async Task<PagedResult<Message>> GetList(string folder, int pageNumber, int pageSize)
        {
            var f = Enum.Parse<MessageFolder>(folder);
            var result = await _messagingService.Mailbox.GetList(f, new PageOptions(pageNumber, pageSize));
            return result;
        }

        [HttpGet("/{id}")]
        public async Task<Message> Get(Guid id)
        {
            var message = await _messagingService.Mailbox.Get(id);
            return message;
        }

        [HttpPost("send")]
        public void Send([FromBody] Message message)
        {
            _messagingService.SendMessage(message);
        }
        
        [HttpDelete("{id}")]
        public async void Delete(Guid id)
        {
            await _messagingService.Mailbox.Delete(id);
        }
        
        [HttpPost]
        public void Save([FromBody] Message message)
        {
            _messagingService.Mailbox.Save(message);
        }
        
    }
}
