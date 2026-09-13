using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Email;
using Odin.Services.Util;
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
    /// Whether this identity's email actually WORKS, as opposed to whether it has been set up.
    ///
    /// `status` answers "how far did setup get" and nothing more, so an identity whose domain
    /// has no MX reports as fully configured while nothing can deliver mail to it. This runs the
    /// same DNS-record and key checks the owner console's Email tab runs, from the same
    /// services, so the two surfaces cannot disagree.
    ///
    /// On demand rather than part of `status`: it does DNS lookups plus outbound HTTPS, and
    /// status is fetched on every login and identity switch.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpGet("health")]
    [ProducesResponseType(typeof(MailAppHealthResult), 200)]
    public async Task<MailAppHealthResult> GetHealth()
    {
        return await emailAppService.GetHealthAsync(HttpContext.RequestAborted);
    }

    /// <summary>
    /// Creates the mailbox. Idempotent, so a client killed mid-setup calls it again rather than
    /// tracking where it got to.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpPost("setup/mailbox")]
    [ProducesResponseType(typeof(MailboxSetupResult), 200)]
    public async Task<MailboxSetupResult> EnsureMailbox([FromBody] EnsureMailboxRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        return await emailAppService.EnsureMailboxAsync(request.PrimaryEmailAddress, WebOdinContext);
    }

    /// <summary>
    /// Generates the identity's OpenPGP keyring, writes it to the email drive, and publishes its
    /// certificate. The last setup step; call it again to rotate.
    ///
    /// The private half is never returned — it is written straight to the drive, so the client
    /// reads it back by the returned unique id and no once-only delivery can be dropped.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpPost("setup/keys")]
    [ProducesResponseType(typeof(EmailKeyGenerationResult), 200)]
    public async Task<EmailKeyGenerationResult> GenerateKey([FromBody] GenerateEmailKeyRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        byte[] entropy = [];
        if (!string.IsNullOrEmpty(request.ClientEntropyBase64))
        {
            try
            {
                entropy = Convert.FromBase64String(request.ClientEntropyBase64);
            }
            catch (FormatException e)
            {
                throw new OdinClientException("ClientEntropyBase64 is not valid base64", inner: e);
            }

            if (entropy.Length is < 32 or > 1024)
            {
                throw new OdinClientException("Client entropy must be between 32 and 1024 bytes");
            }
        }

        return await emailAppService.GenerateKeyAsync(request.PrimaryEmailAddress, entropy, WebOdinContext);
    }

    /// <summary>
    /// Issues a mail-client credential. The secret is returned exactly once — the mail server
    /// generates it and will not show it again — so the caller must persist it before showing it.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpPost("app-passwords")]
    [ProducesResponseType(typeof(AppPasswordIssueResult), 200)]
    public async Task<AppPasswordIssueResult> IssueAppPassword([FromBody] IssueAppPasswordRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        return await emailAppService.IssueAppPasswordAsync(
            request.PrimaryEmailAddress, request.Label, WebOdinContext);
    }

    /// <summary>
    /// Revokes a credential on the mail server. Deleting the client's own record of it revokes
    /// nothing. Idempotent: revoking an unknown id succeeds.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpDelete("app-passwords/{id}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> RevokeAppPassword(string id)
    {
        OdinValidationUtils.AssertIsTrue(!string.IsNullOrWhiteSpace(id), "an app password id is required");
        await emailAppService.RevokeAppPasswordAsync(id, WebOdinContext);
        return NoContent();
    }

    /// <summary>
    /// How the mailbox is doing — unread, junk, storage, and anything stuck on the way out.
    /// Answers Available = false when the mail server does not report, rather than failing.
    /// </summary>
    [SwaggerOperation(Tags = [SwaggerInfo.Mail])]
    [HttpGet("mailbox")]
    [ProducesResponseType(typeof(MailboxStatusResult), 200)]
    public async Task<MailboxStatusResult> GetMailboxStatus()
    {
        return await emailAppService.GetMailboxStatusAsync(WebOdinContext);
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

public class EnsureMailboxRequest
{
    /// <summary>Must be an address at this identity's domain. Defaults to mail@&lt;identity&gt;.</summary>
    public string PrimaryEmailAddress { get; init; } = "";
}

public class IssueAppPasswordRequest
{
    public string PrimaryEmailAddress { get; init; } = "";

    /// <summary>What the credential is for, e.g. "Thunderbird — laptop". Shown back to the user.</summary>
    public string Label { get; init; } = "";
}

public class GenerateEmailKeyRequest
{
    public string PrimaryEmailAddress { get; init; } = "";

    /// <summary>
    /// Optional caller-collected entropy, base64, 32..1024 bytes — the Email setup app collects it
    /// from the phone's accelerometer. Additive only: it is mixed into the server's own OS-seeded
    /// generator, never substituted for it, so a hostile or degenerate value cannot weaken the key.
    /// Empty is normal and expected on desktop and web, where there is no sensor; key generation
    /// is never blocked on one.
    /// </summary>
    public string ClientEntropyBase64 { get; init; } = "";
}
