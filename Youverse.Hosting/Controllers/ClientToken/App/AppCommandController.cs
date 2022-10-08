#nullable enable
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Tenant;

namespace Youverse.Hosting.Controllers.ClientToken.App
{
    [ApiController]
    [Route(AppApiPathConstants.CommandSenderV1)]
    [AuthorizeValidAppExchangeGrant]
    public class AppCommandController : Controller
    {
        private readonly string _currentTenant;
        private readonly CommandMessagingService _commandMessagingService;

        public AppCommandController(ITenantProvider tenantProvider, CommandMessagingService commandMessagingService)
        {
            _commandMessagingService = commandMessagingService;
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
        }
        
        /// <summary>
        /// Sends a command message to 
        /// </summary>
        /// <returns></returns>
        [HttpGet("send")]
        public async Task<CommandMessageResult> SendCommand([FromBody] SendCommandRequest request)
        {
            var results = await _commandMessagingService.SendCommandMessage(request.Command);
            return results;
        }
    }

    public class SendCommandRequest
    {
        public CommandMessage Command { get; set; } 
    }
    
}