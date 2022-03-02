using System;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Configuration;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.YouAuth;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Requests;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    public interface IPerimeterHttpClient
    {
        private const string RootPath = "/api/perimeter";

        [Post(RootPath + "/invitations/connect")]
        Task<ApiResponse<NoResultResponse>> DeliverConnectionRequest([Body] ConnectionRequest request);

        [Post(RootPath + "/invitations/establishconnection")]
        Task<ApiResponse<NoResultResponse>> EstablishConnection([Body] ConnectionRequestReply requestReply);

        [Get(RootPath + "/profile")]
        Task<ApiResponse<DotYouProfile>> GetProfile();

        [Get(RootPath + "/youauth/validate-ac-res")]
        Task<ApiResponse<byte[]>> ValidateAuthorizationCodeResponse(string initiator, string ac);
    }
}