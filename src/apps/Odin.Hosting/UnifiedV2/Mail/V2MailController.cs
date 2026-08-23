using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Email;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Mail;

/// <summary>
/// Email setup for the chat-kmp add-on app (docs/email-keys-plan.md). The app cannot reach owner
/// endpoints — it holds an app token — so the actions it needs live here, authorized by its
/// Read+Write access to the Email app's drive rather than by a permission key.
///
/// The owner surface under <c>/api/owner/v1/mail</c> is unchanged and stays the owner console's.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Mail)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2MailController(EmailAppService emailAppService) : OdinControllerBase
{
    /// <summary>
    /// Everything the app needs to decide what to show: whether this host runs email at all,
    /// whether the caller can use the email drive, and how far setup got.
    ///
    /// Ungated on purpose — the app has to render "this server has no email" before it has a
    /// drive, and telling that apart from "not set up yet" is the whole point of the screen.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpGet("status")]
    [ProducesResponseType(typeof(MailAppStatusResult), 200)]
    public async Task<MailAppStatusResult> GetStatus()
    {
        return await emailAppService.GetStatusAsync(WebOdinContext);
    }

    /// <summary>
    /// Proves this device can still read mail encrypted to the published key: the caller decrypts
    /// the returned message with the keyring on its email drive and compares the hash.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpPost("challenge")]
    [ProducesResponseType(typeof(MailRoundTripChallenge), 200)]
    public async Task<MailRoundTripChallenge> CreateChallenge()
    {
        return await emailAppService.CreateRoundTripChallengeAsync(WebOdinContext);
    }
}
