using System.Collections.Generic;
using System.Threading.Tasks;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Hosting.Controllers.OwnerToken;
using Youverse.Hosting.Controllers.OwnerToken.Circles;

namespace Youverse.Hosting.Tests.OwnerApi.Circle;

public interface ICircleMembershipOwnerClient
{
    private const string RootPath = OwnerApiPathConstants.CirclesV1 + "/membership";

    [Post(RootPath + "/list")]
    Task<ApiResponse<IEnumerable<DotYouIdentity>>> GetMembers([Body] GetCircleMembersRequest circleId);

    [Post(RootPath + "/add")]
    Task<ApiResponse<bool>> AddMembers([Body] AddCircleMembershipRequest request);
        
    [Post(RootPath + "/remove")]
    Task<ApiResponse<bool>> RemoveMembers([Body] RemoveCircleMembershipRequest request);
}