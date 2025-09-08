using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;
using Odin.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityV1)]
[AuthorizeValidOwnerToken]
public class OwnerSecurityController : OdinControllerBase
{
    private readonly PasswordKeyRecoveryService _recoveryService;
    private readonly OwnerSecretService _ss;
    private readonly OwnerAuthenticationService _ownerAuthenticationService;


    /// <summary />
    public OwnerSecurityController(PasswordKeyRecoveryService recoveryService, OwnerSecretService ss,
        OwnerAuthenticationService ownerAuthenticationService)
    {
        _recoveryService = recoveryService;
        _ss = ss;
        _ownerAuthenticationService = ownerAuthenticationService;
    }

    /// <summary>
    /// Returns redacted security information for the currently logged in user
    /// </summary>
    /// <returns></returns>
    [HttpGet("context")]
    public RedactedOdinContext GetSecurityContext()
    {
        return WebOdinContext.Redacted();
    }

    [HttpGet("recovery-key")]
    public async Task<DecryptedRecoveryKey> GetAccountRecoveryKey()
    {
        return await _recoveryService.GetKeyAsync(byPassWaitingPeriod: false, WebOdinContext);
    }

    [HttpPost("resetpasswd")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await _ss.ResetPasswordAsync(request, WebOdinContext);
        return new OkResult();
    }

    [HttpGet("account-status")]
    public async Task<AccountStatusResponse> GetAccountStatus()
    {
        return await _ownerAuthenticationService.GetAccountStatusAsync(WebOdinContext);
    }

    [HttpPost("delete-account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        //validate owner password
        await _ownerAuthenticationService.MarkForDeletionAsync(request.CurrentAuthenticationPasswordReply, WebOdinContext);
        return new OkResult();
    }

    [HttpPost("undelete-account")]
    public async Task<IActionResult> UndeleteAccount([FromBody] DeleteAccountRequest request)
    {
        //validate owner password
        await _ownerAuthenticationService.UnmarkForDeletionAsync(request.CurrentAuthenticationPasswordReply, WebOdinContext);
        return new OkResult();
    }
}