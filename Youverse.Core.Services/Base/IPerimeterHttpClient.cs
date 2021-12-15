using System;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Configuration;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    
    //TODO: need to evaluate if we want other apps to use these methods.
    [Obsolete("Need to replace all calls with Transit subsystem")]
    public interface IPerimeterHttpClient
    {
        private const string RootPath = "/api/perimeter";

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<NoResultResponse>> DeliverConnectionRequest([Body] ConnectionRequest request);

        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] AcknowledgedConnectionRequest request);

        [Get(RootPath + "/profile")]
        Task<ApiResponse<DotYouProfile>> GetProfile();

        [Get(YouAuthDefaults.ValidateAuthorizationCodeResponsePath)]
        Task<ApiResponse<string>> ValidateAuthorizationCodeResponse(string initiator, string ac);
    }
}