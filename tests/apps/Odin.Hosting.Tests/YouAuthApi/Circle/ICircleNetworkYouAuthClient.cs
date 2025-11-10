using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Membership.Connections;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Refit;

namespace Odin.Hosting.Tests.YouAuthApi.Circle
{
    public interface ICircleNetworkYouAuthClient
    {
        private const string root_path = GuestApiPathConstantsV1.CirclesV1 + "/connections";
        
        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int count, string cursor, bool omitContactData = true);
    }
}