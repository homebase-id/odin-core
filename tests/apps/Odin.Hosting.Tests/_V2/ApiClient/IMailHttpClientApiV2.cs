using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Email;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public interface IMailHttpClientApiV2
{
    private const string Root = UnifiedApiRouteConstants.Mail;

    [Get(Root + "/status")]
    Task<ApiResponse<MailAppStatusResult>> GetStatus();

    [Post(Root + "/challenge")]
    Task<ApiResponse<MailRoundTripChallenge>> CreateChallenge();
}
