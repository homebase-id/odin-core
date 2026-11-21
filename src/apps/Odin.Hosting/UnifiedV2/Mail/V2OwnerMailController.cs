using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.Mail;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Email;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Mail;

/// <summary>
/// Email activation and mailbox management for the owner console: V2 of <c>OwnerMailController</c>, same services.
/// </summary>
/// <remarks>
/// Under <c>/mail/owner</c> because <c>/mail/status</c> and <c>/mail/challenge</c> are <see cref="V2MailController"/>'s,
/// the app's, on another service. These services take no caller at all, so the policy is the only thing that keeps an
/// app out.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.MailOwner)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2OwnerMailController(
    MailActivationService mailActivationService,
    EmailHealthVerifier emailHealthVerifier) : OdinControllerBase
{
    [HttpPost("activate")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<MailActivationResult> Activate([FromBody] ActivateMailRequest request)
    {
        return await mailActivationService.ActivateAsync(request.PublicCertificateArmored, request.PrimaryEmailAddress);
    }

    [HttpGet("status")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<MailStatusResult> GetStatus()
    {
        return await mailActivationService.GetStatusAsync();
    }

    [HttpPost("publish-dns-records")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<MailDnsPublishResult> PublishDnsRecords()
    {
        return await mailActivationService.PublishMailDnsRecordsAsync();
    }

    [HttpGet("verify")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<EmailHealthVerifier.Result> Verify()
    {
        return await emailHealthVerifier.VerifyAsync(HttpContext.RequestAborted);
    }

    [HttpPost("challenge")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<MailRoundTripChallenge> CreateChallenge()
    {
        return await mailActivationService.CreateRoundTripChallengeAsync();
    }

    [HttpPost("app-password")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<AppPasswordResponse> ProvisionAppPassword([FromBody] AppPasswordRequest request)
    {
        var password = await mailActivationService.ProvisionAppPasswordAsync(request.PrimaryEmailAddress, request.Label);
        // Shown exactly once; not retrievable afterwards
        return new AppPasswordResponse { Password = password };
    }
}
