using System.Threading.Tasks;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.Base;
using Refit;

namespace Odin.Hosting.Tests.Anonymous.Ident
{
    /// <summary>
    /// The interface for storing files
    /// </summary>
    public interface IIdentHttpClient
    {
        [Get(YouAuthApiPathConstants.AuthV1 + "/ident")]
        Task<ApiResponse<GetIdentResponse>> GetIdent();
    }
}