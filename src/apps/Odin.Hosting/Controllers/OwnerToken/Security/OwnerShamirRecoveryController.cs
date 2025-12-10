using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("initiate-recovery-mode")]
    public async Task<IActionResult> InitiateRecoveryMode()
    {
        await _recoveryService.InitiateRecoveryModeEntry(WebOdinContext);
        return Ok();
    }

    [HttpGet("verify-enter")]
    public async Task<IActionResult> VerifyEnterRecoveryMode([FromQuery] string id, [FromQuery] string token)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(id, nameof(id));
        // OdinValidationUtils.AssertNotNullOrEmpty(redirect, nameof(redirect));

        await _recoveryService.EnterRecoveryMode(Guid.Parse(id), token, WebOdinContext);

        const string redirect = "/owner/shamir-account-recovery?fv=1";
        return Redirect(redirect);
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

    [HttpPost("finalize")]
    public async Task<IActionResult> FinalizeRecovery([FromBody] FinalRecoveryRequest request)
    {
        OdinValidationUtils.AssertNotNullOrEmpty(request.Id, nameof(request.Id));
        await _recoveryService.FinalizeRecovery(
            Guid.Parse(request.Id),
            Guid.Parse(request.FinalKey), 
            request.PasswordReply,
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