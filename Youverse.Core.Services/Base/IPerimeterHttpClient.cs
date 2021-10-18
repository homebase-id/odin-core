using System.Threading.Tasks;
using Refit;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Core.Services.Base
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