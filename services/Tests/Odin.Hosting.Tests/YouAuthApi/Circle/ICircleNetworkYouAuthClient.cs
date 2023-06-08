using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Hosting.Controllers.Anonymous;
using Youverse.Hosting.Controllers.OwnerToken.Circles;

namespace Youverse.Hosting.Tests.YouAuthApi.Circle
{
    public interface ICircleNetworkYouAuthClient
    {
        private const string root_path = YouAuthApiPathConstants.CirclesV1 + "/connections";
        
        [Get(root_path + "/connected")]
        Task<ApiResponse<PagedResult<RedactedIdentityConnectionRegistration>>> GetConnectedProfiles(int count, long cursor, bool omitContactData = true);
    }
}