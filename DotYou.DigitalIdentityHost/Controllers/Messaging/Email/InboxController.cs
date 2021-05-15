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
    [Route("api/messages/inbox")]
    [ApiController]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class InboxController : ControllerBase
    {
        IMessagingService _messagingService;
        public InboxController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpGet]
        public async Task<PagedResult<Message>> GetList()
        {
            var result = await _messagingService.Inbox.GetList(PageOptions.Default);
            return result;
        }

        [HttpGet("{id}")]
        public async Task<Message> Get(Guid id)
        {
            var message = await _messagingService.Inbox.Get(id);
            return message;
        }

        /// <summary>
        /// POSTing to the Inbox saves the message for the current tenant
        /// </summary>
        [HttpPost()]
        public void Post([FromBody] Message message)
        {
            /// POST TO /api/messages/inbox
            //TODO: determine how to secure this
            // perhaps the rules of the messaging service will be sufficient as it
            // has to scan many items.
            message.Received = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _messagingService.Inbox.Save(message);
        }
        
        [HttpDelete("{id}")]
        public async void Delete(Guid id)
        {
            await _messagingService.Inbox.Delete(id);
        }
    }
}
