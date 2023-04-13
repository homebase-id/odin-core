using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Controllers.ClientToken.Transit;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    /// <summary>
    /// The interface for 
    /// </summary>
    public interface ITransitTestAppHttpClient
    {
        private const string RootEndpoint = AppApiPathConstants.TransitV1 + "/app";


        [Post(RootEndpoint + "/process")]
        Task<ApiResponse<bool>> ProcessInbox([Body] ProcessInboxRequest request);
    }
}