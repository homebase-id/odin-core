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
    private readonly RecoveryService _recoveryService;
    private readonly OwnerSecretService _ss;
    private readonly OwnerAuthenticationService _ownerAuthenticationService;
    private readonly TenantSystemStorage _tenantSystemStorage;

    /// <summary />
    public OwnerSecurityController(RecoveryService recoveryService, OwnerSecretService ss,
        OwnerAuthenticationService ownerAuthenticationService, TenantSystemStorage tenantSystemStorage)
    {
        _recoveryService = recoveryService;
        _ss = ss;
        _ownerAuthenticationService = ownerAuthenticationService;
        _tenantSystemStorage = tenantSystemStorage;
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
        var db = _tenantSystemStorage.IdentityDatabase;
        return await _recoveryService.GetKeyAsync(WebOdinContext);
    }

    [HttpPost("resetpasswd")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var db = _tenantSystemStorage.IdentityDatabase;
        await _ss.ResetPasswordAsync(request, WebOdinContext, db);
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