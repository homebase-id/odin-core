using System;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Services.Transit.Incoming;
using Youverse.Hosting.Controllers.ClientToken;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    /// <summary>
    /// The interface for 
    /// </summary>
    public interface ITransitTestAppHttpClient
    {
        private const string RootEndpoint = AppApiPathConstants.TransitV1 + "/app";

        [Obsolete("TODO: replace with new outbox process")]
        [Post(RootEndpoint + "/process")]
        Task<ApiResponse<bool>> ProcessTransfers();
    }
}