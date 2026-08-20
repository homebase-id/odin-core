using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Email;

namespace Odin.Hosting.Controllers.OwnerToken.Mail
{
    /// <summary>
    /// Email activation and mailbox management (docs/email-keys-plan.md). Called by
    /// the app AFTER it has created the email drive and stored the keypair on it -
    /// only the PUBLIC certificate crosses this API.
    /// </summary>
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.MailV1)]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerMailController(MailActivationService mailActivationService) : OdinControllerBase
    {
        [HttpPost("activate")]
        public async Task<MailActivationResult> Activate([FromBody] ActivateMailRequest request)
        {
            return await mailActivationService.ActivateAsync(request.PublicCertificateArmored, request.PrimaryEmailAddress);
        }

        [HttpGet("status")]
        public async Task<MailStatusResult> GetStatus()
        {
            return await mailActivationService.GetStatusAsync();
        }

        [HttpPost("app-password")]
        public async Task<AppPasswordResponse> ProvisionAppPassword([FromBody] AppPasswordRequest request)
        {
            var password = await mailActivationService.ProvisionAppPasswordAsync(request.PrimaryEmailAddress, request.Label);
            // Shown exactly once; not retrievable afterwards
            return new AppPasswordResponse { Password = password };
        }
    }

    public class ActivateMailRequest
    {
        public string PublicCertificateArmored { get; init; } = "";
        public string PrimaryEmailAddress { get; init; } = "";
    }

    public class AppPasswordRequest
    {
        public string PrimaryEmailAddress { get; init; } = "";
        public string Label { get; init; } = "";
    }

    public class AppPasswordResponse
    {
        public string Password { get; init; } = "";
    }
}
