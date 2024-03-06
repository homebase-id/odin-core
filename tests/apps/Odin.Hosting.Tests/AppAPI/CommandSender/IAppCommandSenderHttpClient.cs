using System.Threading.Tasks;
using Odin.Core.Services.Apps.CommandMessaging;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Hosting.Controllers.ClientToken.App.Commands;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.CommandSender
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IAppCommandSenderHttpClient
    {
        private const string RootEndpoint = AppApiPathConstants.CommandSenderV1;

        [Post(RootEndpoint + "/send")]
        Task<ApiResponse<CommandMessageResult>> SendCommand([Body] SendCommandRequest request);

        [Post(RootEndpoint + "/unprocessed")]
        Task<ApiResponse<ReceivedCommandResultSet>> GetUnprocessedCommands([Body]GetUnprocessedCommandsRequest request);

        [Post(RootEndpoint + "/markcompleted")]
        Task<ApiResponse<bool>> MarkCommandsComplete([Body] MarkCommandsCompleteRequest request);
    }
}