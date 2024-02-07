#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Apps.CommandMessaging;
using Odin.Core.Services.Base;
using Odin.Hosting.Controllers.Base;

namespace Odin.Hosting.Controllers.ClientToken.App.Commands
{
    [ApiController]
    [Route(AppApiPathConstants.CommandSenderV1)]
    [AuthorizeValidAppToken]
    public class AppCommandController(CommandMessagingService commandMessagingService, OdinContextAccessor contextAccessor)
        : OdinControllerBase
    {
        /// <summary>
        /// Sends a command message to a set of recipients
        /// </summary>
        /// <returns></returns>
        [HttpPost("send")]
        public async Task<CommandMessageResult> SendCommand([FromBody] SendCommandRequest request)
        {
            var driveId = contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive);

            AssertValidRecipientList(request.Command.Recipients);

            var results = await commandMessagingService.SendCommandMessage(driveId, request.Command);
            return results;
        }

        /// <summary>
        /// Gets commands and their associated files which need to be processed
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("unprocessed")]
        public async Task<ReceivedCommandResultSet> GetUnprocessedCommands([FromBody] GetUnprocessedCommandsRequest request)
        {
            var driveId = contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive);
            var result = await commandMessagingService.GetUnprocessedCommands(driveId, request.Cursor);
            return result;
        }

        [HttpPost("markcompleted")]
        public async Task<bool> MarkCommandsCompleted([FromBody] MarkCommandsCompleteRequest request)
        {
            var driveId = contextAccessor.GetCurrent().PermissionsContext.GetDriveId(request.TargetDrive);

            await commandMessagingService.MarkCommandsProcessed(driveId, request.CommandIdList.ToList());
            return true;
        }
    }
}