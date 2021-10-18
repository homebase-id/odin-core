using System.IO;
using System.Threading.Tasks;
using DotYou.Kernel.Services.MediaService;
using DotYou.Types;
using DotYou.Types.Circle;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;

namespace DotYou.Kernel.HttpClient
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    
    //TODO: need to evaluate if we want other apps to use these methods.
    public interface IPerimeterHttpClient
    {
        private const string RootPath = "/api/perimeter";

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<NoResultResponse>> DeliverConnectionRequest([Body] ConnectionRequest request);

        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] AcknowledgedConnectionRequest request);

        [Get(RootPath + "/profile")]
        Task<ApiResponse<DotYouProfile>> GetProfile();
    }
}