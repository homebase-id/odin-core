#nullable enable
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration.CommandLine;
using Swashbuckle.AspNetCore.Annotations;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Base;
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
        private readonly DotYouContextAccessor _contextAccessor;

        public AppCommandController(ITenantProvider tenantProvider, CommandMessagingService commandMessagingService, DotYouContextAccessor contextAccessor)
        {
            _commandMessagingService = commandMessagingService;
            _contextAccessor = contextAccessor;
            _currentTenant = tenantProvider.GetCurrentTenant()!.Name;
        }

        /// <summary>
        /// Sends a command message to a set of recipients
        /// </summary>
        /// <returns></returns>
        [HttpPost("send")]
        public async Task<CommandMessageResult> SendCommand([FromBody] SendCommandRequest request)
        {
            var results = await _commandMessagingService.SendCommandMessage(request.Command);
            return results;
        }

        /// <summary>
        /// Gets commands and their associated files which need to be processed
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("unprocessed")]
        public async Task<ReceivedCommandResultSet> GetUnprocessedCommands([FromBody] GetUnproccessedCommandsRequest request)
        {
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive);
            var result = await _commandMessagingService.GetUnprocessedCommands(driveId, request.Cursor);
            return result;
        }

        [HttpPost("markcompleted")]
        public async Task<bool> MarkCommandsCompleted([FromBody] MarkCommandsCompleteRequest request)
        {
            await _commandMessagingService.MarkCommandsProcessed(request.CommandIdList);
            return true;
        }
    }
}