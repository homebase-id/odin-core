using System;
using DotYou.Kernel.Services.Messaging.Email;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Mvc;

namespace DotYou.TenantHost.Controllers.Incoming
{
    [ApiController]
    [Route("api/incoming/messages")]
    public class EmailMessageController : ControllerBase
    {
        IMessagingService _messagingService;
        public EmailMessageController(IMessagingService messagingService)
        {
            _messagingService = messagingService;
        }

        [HttpPost("email")]
        public void ReceiveIncomingEmailMessage([FromBody] Message message)
        {
            //TODO: Move this to a generic interface that sets it for all other incoming classes
            message.Received = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _messagingService.SaveMessage(message);
        }
    }
}
