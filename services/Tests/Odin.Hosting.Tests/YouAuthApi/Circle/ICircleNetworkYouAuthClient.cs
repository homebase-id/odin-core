using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Services.Membership.Connections;
using Odin.Hosting.Controllers.Anonymous;
using Refit;

namespace Odin.Hosting.Tests.YouAuthApi.Circle
{
    public interface ICircleNetworkYouAuthClient
    {
        private const string root_path = YouAuthApiPathConstants.CirclesV1 + "/connections";
        
        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int count, long cursor, bool omitContactData = true);
    }
}