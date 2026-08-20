using System.Threading.Tasks;
using Odin.Hosting.Controllers.OwnerToken.Mail;
using Odin.Hosting.Tests.OwnerApi.Utils;
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
}

public class MailApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<ApiResponse<MailActivationResult>> Activate(string publicCertificateArmored, string primaryEmailAddress)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IMailTestHttpClientForOwner>(client, sharedSecret);
        return await svc.Activate(new ActivateMailRequest
        {
            PublicCertificateArmored = publicCertificateArmored,
            PrimaryEmailAddress = primaryEmailAddress,
        });
    }

    public async Task<ApiResponse<MailStatusResult>> GetStatus()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IMailTestHttpClientForOwner>(client, sharedSecret);
        return await svc.GetStatus();
    }

    public async Task<ApiResponse<AppPasswordResponse>> ProvisionAppPassword(string primaryEmailAddress, string label)
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IMailTestHttpClientForOwner>(client, sharedSecret);
        return await svc.ProvisionAppPassword(new AppPasswordRequest
        {
            PrimaryEmailAddress = primaryEmailAddress,
            Label = label,
        });
    }
}
