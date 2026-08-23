using System.Net.Http;
using System.Threading.Tasks;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Mail;
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

    [Post(Root + "/setup/mailbox")]
    Task<ApiResponse<MailboxSetupResult>> EnsureMailbox([Body] EnsureMailboxRequest request);

    [Post(Root + "/app-passwords")]
    Task<ApiResponse<AppPasswordIssueResult>> IssueAppPassword([Body] IssueAppPasswordRequest request);

    [Delete(Root + "/app-passwords/{id}")]
    Task<ApiResponse<HttpContent>> RevokeAppPassword(string id);

    [Get(Root + "/storage")]
    Task<ApiResponse<MailStorageResult>> GetStorage();
}
