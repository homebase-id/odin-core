using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Membership.Connections.Requests;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IIntroductionsHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Connections;

    [Post(Root + "/introductions/preflight")]
    Task<ApiResponse<IntroductionPreflightResult>> PreflightIntroductions([Body] IntroductionGroup group);
}
