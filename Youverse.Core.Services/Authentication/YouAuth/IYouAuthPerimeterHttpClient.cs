using System.Threading.Tasks;
using Refit;

namespace Youverse.Core.Services.Authentication.YouAuth
{
    /// <summary>
    /// Sends outgoing invitations, email messages, and chat messages to other Digital Identities
    /// </summary>
    public interface IYouAuthPerimeterHttpClient
    {
        private const string RootPath = "/api/perimeter";
        
        [Get(RootPath + "/youauth/validate-ac-res")]
        Task<ApiResponse<byte[]>> ValidateAuthorizationCodeResponse(string initiator, string ac);
    }
}