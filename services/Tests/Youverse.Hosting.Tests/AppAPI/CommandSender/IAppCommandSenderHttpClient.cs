using System.Net.Http;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.App;
using Youverse.Hosting.Controllers.ClientToken.Drive;

namespace Youverse.Hosting.Tests.AppAPI.CommandSender
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
        Task<ApiResponse<ReceivedCommandResultSet>> GetUnprocessedCommands([Body]GetUnproccessedCommandsRequest request);

        [Post(RootEndpoint + "/markcompleted")]
        Task<ApiResponse<bool>> MarkCommandsComplete([Body] MarkCommandsCompleteRequest request);
    }
}