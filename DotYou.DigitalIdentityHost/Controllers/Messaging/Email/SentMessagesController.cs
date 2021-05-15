﻿using System;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Messaging.Email
{

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
            var result = await _messagingService.SentItems.GetList(PageOptions.Default);
            return result;
        }

        [HttpGet("{id}")]
        public async Task<Message> Get(Guid id)
        {
            var message = await _messagingService.SentItems.Get(id);
            return message;
        }

        /// <summary>
        /// POSTing to the Inbox saves the message for the current tenant
        /// </summary>
        [HttpPost()]
        public void Post([FromBody] Message message)
        {
            _messagingService.SentItems.Save(message);
        }
        
        [HttpDelete("{id}")]
        public async void Delete(Guid id)
        {
            await _messagingService.SentItems.Delete(id);
        }
    }
}
