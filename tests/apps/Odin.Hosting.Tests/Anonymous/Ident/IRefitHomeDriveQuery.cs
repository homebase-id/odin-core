using System.Threading.Tasks;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Refit;

namespace Odin.Hosting.Tests.Anonymous.Ident
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IIdentHttpClient
    {
        [Get(GuestApiPathConstantsV1.AuthV1 + "/ident")]
        Task<ApiResponse<GetIdentResponse>> GetIdent();
    }
}