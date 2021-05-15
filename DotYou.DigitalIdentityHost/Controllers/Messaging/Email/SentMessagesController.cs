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
    /// Sends messages
    /// </summary>
    [Route("api/messages/sent")]
    [ApiController]
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class SentMessagesController : ControllerBase
    {
        IMessagingService _messagingService;
        public SentMessagesController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpGet]
        public async Task<PagedResult<Message>> GetList()
        {
            throw new NotImplementedException();
        }

        [HttpGet("{id}")]
        public async Task<Message> Get(Guid id)
        {
            throw new NotImplementedException();
        }

        [HttpPost]
        public void Post([FromBody] Message message)
        {
            _messagingService.SendMessage(message);
        }

        [HttpPut("{id}")]
        public void Put(Guid id, [FromBody] Message message)
        {
            throw new NotImplementedException();
        }

        // DELETE api/messages/send/5
        [HttpDelete("{id}")]
        public async void Delete(Guid id)
        {
            throw new NotImplementedException();
        }
    }
}
