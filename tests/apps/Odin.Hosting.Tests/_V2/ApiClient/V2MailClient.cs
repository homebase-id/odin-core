using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.UnifiedV2.Mail;
using Odin.Services.Email;
using Refit;

namespace Odin.Hosting.Tests._V2.ApiClient;

public class V2MailClient(OdinId identity, IApiClientFactory factory)
{
    private IMailHttpClientApiV2 Service()
    {
        var client = factory.CreateHttpClient(identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<IMailHttpClientApiV2>(client, sharedSecret);
    }

    public async Task<ApiResponse<MailAppStatusResult>> GetStatusAsync() =>
        await Service().GetStatus();

    public async Task<ApiResponse<MailAppHealthResult>> GetHealthAsync() =>
        await Service().GetHealth();

    public async Task<ApiResponse<MailRoundTripChallenge>> CreateChallengeAsync() =>
        await Service().CreateChallenge();

    public async Task<ApiResponse<MailboxSetupResult>> EnsureMailboxAsync(string primaryEmailAddress) =>
        await Service().EnsureMailbox(new EnsureMailboxRequest { PrimaryEmailAddress = primaryEmailAddress });

    public async Task<ApiResponse<EmailKeyGenerationResult>> GenerateKeyAsync(
        string primaryEmailAddress,
        string clientEntropyBase64 = "") =>
        await Service().GenerateKey(new GenerateEmailKeyRequest
        {
            PrimaryEmailAddress = primaryEmailAddress,
            ClientEntropyBase64 = clientEntropyBase64,
        });

    public async Task<ApiResponse<AppPasswordIssueResult>> IssueAppPasswordAsync(string primaryEmailAddress, string label) =>
        await Service().IssueAppPassword(new IssueAppPasswordRequest
        {
            PrimaryEmailAddress = primaryEmailAddress,
            Label = label,
        });

    public async Task<ApiResponse<HttpContent>> RevokeAppPasswordAsync(string id) =>
        await Service().RevokeAppPassword(id);

    public async Task<ApiResponse<MailboxStatusResult>> GetMailboxStatusAsync() =>
        await Service().GetMailboxStatus();
}
