using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Util;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityRecoveryV1)]
[ApiExplorerSettings(GroupName = "owner-v1")]
public class OwnerShamirRecoveryController : OdinControllerBase
{
    private readonly ShamirRecoveryService _recoveryService;

    /// <summary />
    public OwnerShamirRecoveryController(ShamirRecoveryService recoveryService)
    {
        _recoveryService = recoveryService;
    }

    [HttpGet("status")]
    public async Task<ShamirRecoveryStatusRedacted> GetRecoveryStatus()
    {
        var status = await _recoveryService.GetStatus(WebOdinContext);
        return status;
    }

    /// <summary>
    /// Starts recovery.  The browser sends its throwaway public key, and must finish recovery itself:
    /// every holder seals its shard to that key.
    /// </summary>
    [HttpPost("initiate-recovery-mode")]
    public async Task<IActionResult> InitiateRecoveryMode(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] InitiateRecoveryModeRequest request)
    {
        await _recoveryService.InitiateRecoveryModeEntry(request?.RecoveryPublicKeyJwk, WebOdinContext);
        return Ok();
    }

    /// <summary>
    /// The email's link.  It only opens the recovery page, which posts the nonce back with its throwaway
    /// public key (<see cref="EnterRecoveryMode"/>): entering recovery must come from the browser that
    /// started it.
    /// </summary>
    [HttpGet("verify-enter")]
    public IActionResult VerifyEnterRecoveryMode([FromQuery] string id)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(id, nameof(id));
        return Redirect($"/owner/shamir-account-recovery?enter={Uri.EscapeDataString(id)}");
    }

    [HttpPost("verify-enter")]
    public async Task<IActionResult> EnterRecoveryMode([FromBody] EnterRecoveryModeRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Id, nameof(request.Id));
        await _recoveryService.EnterRecoveryMode(Guid.Parse(request.Id), request.RecoveryPublicKeyJwk, WebOdinContext);
        return Ok();
    }

    /// <summary>
    /// An automatic holder's email link, clicked on any device: the page posts its shard and code here,
    /// and the holder releases the shard sealed to the recovering browser's key.
    /// </summary>
    [HttpPost("fill-slot")]
    public async Task<IActionResult> FillSlot([FromBody] FillSlotRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.ShardId, nameof(request.ShardId));
        await _recoveryService.FillSlotAsync(Guid.Parse(request.ShardId), request.Code, WebOdinContext);
        return Ok();
    }

    /// <summary>
    /// Once enough shards are in: the shards, the dealer's keys and the deal's copies, sealed to the
    /// throwaway key of the browser that started recovery.
    /// </summary>
    [HttpGet("recovery-bundle")]
    public async Task<ShamirRecoveryBundleResponse> GetRecoveryBundle()
    {
        return await _recoveryService.GetRecoveryBundleAsync(WebOdinContext);
    }

    [HttpPost("exit-recovery-mode")]
    public async Task<IActionResult> InitiateExitRecoveryMode()
    {
        await _recoveryService.InitiateRecoveryModeExit(WebOdinContext);
        return Ok();
    }

    [HttpGet("verify-exit")]
    public async Task<IActionResult> VerifyExitRecoveryMode([FromQuery] string id, [FromQuery] string token)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(id, nameof(id));
        await _recoveryService.ExitRecoveryMode(Guid.Parse(id), token, WebOdinContext);

        const string redirect = "/owner/login";
        return Redirect(redirect);
    }

    /// <summary>
    /// A server-made deal's recovery: the secret the recovering browser rebuilt, and a new password.
    /// </summary>
    [HttpPost("finalize")]
    public async Task<IActionResult> FinalizeRecovery([FromBody] FinalRecoveryRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Secret64, nameof(request.Secret64));
        await _recoveryService.FinalizeServerMadeDealAsync(Convert.FromBase64String(request.Secret64), request.PasswordReply,
            WebOdinContext);
        return Ok();
    }
    
    [HttpGet("verify-email-fwd")]
    public IActionResult VerifyRecoveryEmailRedirector([FromQuery] string id)
    {
        var link = $"{OwnerApiPathConstants.SecurityRecoveryV1}/verify-email?id={id}";
    
        var html = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
  <title>Email Verification</title>
  <style>
    body {{
      font-family: system-ui, sans-serif;
      display: flex;
      flex-direction: column;
      justify-content: center;
      align-items: center;
      height: 100vh;
      margin: 0;
      background-color: #f9fafb;
      color: #111827;
    }}
    .container {{
      background: white;
      padding: 2rem 3rem;
      border-radius: 0.75rem;
      box-shadow: 0 4px 12px rgba(0,0,0,0.08);
      text-align: center;
      max-width: 400px;
    }}
    h1 {{
      font-size: 1.25rem;
      margin-bottom: 1rem;
    }}
    p {{
      margin-bottom: 1.5rem;
      color: #374151;
    }}
    a.button {{
      display: inline-block;
      padding: 0.75rem 1.5rem;
      background-color: #2563eb;
      color: white;
      border-radius: 0.5rem;
      text-decoration: none;
      font-weight: 500;
      transition: background-color 0.2s ease;
    }}
    a.button:hover {{
      background-color: #1e40af;
    }}
  </style>
</head>
<body>
  <div class=""container"">
    <h1>Verify your email</h1>
    <p>Click below to continue.</p>
    <a class=""button"" href=""{link}"">Continue</a>
  </div>
</body>
</html>";

        return Content(html, "text/html");
    }

}