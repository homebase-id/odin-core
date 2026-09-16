using System.Threading.Tasks;
using Odin.Hosting.Controllers.OwnerToken.Mail;
using Odin.Services.Authentication.Owner;
using Odin.Services.Email;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Mail;

public interface IMailTestHttpClientForOwner
{
    private const string Endpoint = OwnerApiPathConstants.MailV1;

    [Post(Endpoint + "/activate")]
    Task<ApiResponse<MailActivationResult>> Activate([Body] ActivateMailRequest request);

    [Get(Endpoint + "/status")]
    Task<ApiResponse<MailStatusResult>> GetStatus();

    [Post(Endpoint + "/app-password")]
    Task<ApiResponse<AppPasswordResponse>> ProvisionAppPassword([Body] AppPasswordRequest request);

    [Get(Endpoint + "/verify")]
    Task<ApiResponse<EmailHealthVerifier.Result>> Verify();

    [Post(Endpoint + "/publish-dns-records")]
    Task<ApiResponse<MailDnsPublishResult>> PublishDnsRecords();

    [Post(Endpoint + "/challenge")]
    Task<ApiResponse<MailRoundTripChallenge>> CreateChallenge();
}
