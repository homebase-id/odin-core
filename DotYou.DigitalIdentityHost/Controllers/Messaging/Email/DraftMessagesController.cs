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

    [Route("api/messages/drafts")]
    [ApiController]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class DraftMessagesController : ControllerBase
    {
        IMessagingService _messagingService;
        public DraftMessagesController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpGet]
        public async Task<PagedResult<Message>> GetList()
        {
            var result = await _messagingService.Drafts.GetList(PageOptions.Default);
            return result;
        }

        [HttpGet("{id}")]
        public async Task<Message> Get(Guid id)
        {
            var message = await _messagingService.Drafts.Get(id);
            return message;
        }

        /// <summary>
        /// POSTing to the Inbox saves the message for the current tenant
        /// </summary>
        [HttpPost()]
        public void Post([FromBody] Message message)
        {
            _messagingService.Drafts.Save(message);
        }
        
        [HttpDelete("{id}")]
        public async void Delete(Guid id)
        {
            await _messagingService.Drafts.Delete(id);
        }
    }
}
