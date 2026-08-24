using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Email;

namespace Odin.Hosting.Controllers.OwnerToken.Mail
{
    /// <summary>
    /// Email activation and mailbox management for the OWNER CONSOLE (docs/email-keys-plan.md).
    /// On this path the caller already holds a keypair and only the PUBLIC certificate crosses
    /// the API.
    ///
    /// The app path is different and lives at /api/v2/mail (see EmailAppService): chat-kmp runs
    /// on an app token, so the server generates the keyring there and writes it straight to the
    /// email drive.
    /// </summary>
    [ApiController]
    [AuthorizeValidOwnerToken]
    [Route(OwnerApiPathConstants.MailV1)]
    [ApiExplorerSettings(GroupName = "owner-v1")]
    public class OwnerMailController(
        MailActivationService mailActivationService,
        EmailHealthVerifier emailHealthVerifier) : OdinControllerBase
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

        /// <summary>
        /// On-demand live verification for the Email tab: DKIM pair proof against the
        /// live DNS TXT + public-key drift across the publication surfaces.
        /// </summary>
        [HttpGet("verify")]
        public async Task<EmailHealthVerifier.Result> Verify()
        {
            return await emailHealthVerifier.VerifyAsync(HttpContext.RequestAborted);
        }

        /// <summary>
        /// Server half of the encrypt/decrypt round-trip check: the client decrypts
        /// the returned OpenPGP message with the keyring from the email drive and
        /// compares the hash. See MailActivationService.CreateRoundTripChallengeAsync.
        /// </summary>
        [HttpPost("challenge")]
        public async Task<MailRoundTripChallenge> CreateChallenge()
        {
            return await mailActivationService.CreateRoundTripChallengeAsync();
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
