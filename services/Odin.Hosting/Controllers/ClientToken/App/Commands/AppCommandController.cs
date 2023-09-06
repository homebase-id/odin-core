#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Apps.CommandMessaging;
using Odin.Core.Services.Base;
using Odin.Core.Services.Tenant;

namespace Odin.Hosting.Controllers.ClientToken.App.Commands
{
    [ApiController]
    [Route(AppApiPathConstants.CommandSenderV1)]
    [AuthorizeValidAppExchangeGrant]
    public class AppCommandController : Controller
    {
        private readonly string _currentTenant;
        private readonly CommandMessagingService _commandMessagingService;
        private readonly OdinContextAccessor _contextAccessor;

        public AppCommandController(ITenantProvider tenantProvider, CommandMessagingService commandMessagingService, OdinContextAccessor contextAccessor)
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
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive);
            var results = await _commandMessagingService.SendCommandMessage(driveId, request.Command);
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
            var driveId = _contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive);

            await _commandMessagingService.MarkCommandsProcessed(driveId, request.CommandIdList.ToList());
            return true;
        }
    }
}