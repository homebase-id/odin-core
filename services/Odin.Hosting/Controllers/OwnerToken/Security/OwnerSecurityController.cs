using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Base;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

/// <summary>
/// Security information for the current user
/// </summary>
[ApiController]
[Route(OwnerApiPathConstants.SecurityV1)]
[AuthorizeValidOwnerToken]
public class OwnerSecurityController : Controller
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly RecoveryService _recoveryService;
    private readonly OwnerSecretService _ss;

    /// <summary />
    public OwnerSecurityController(OdinContextAccessor contextAccessor, RecoveryService recoveryService, OwnerSecretService ss)
    {
        _contextAccessor = contextAccessor;
        _recoveryService = recoveryService;
        _ss = ss;
    }

    /// <summary>
    /// Returns redacted security information for the currently logged in user
    /// </summary>
    /// <returns></returns>
    [HttpGet("context")]
    public RedactedOdinContext GetSecurityContext()
    {
        return _contextAccessor.GetCurrent().Redacted();
    }

    [HttpGet("recovery-key")]
    public async Task<DecryptedRecoveryKey> GetAccountRecoveryKey()
    {
        return await _recoveryService.GetKey();
    }
    
    [HttpPost("resetpasswd")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest reply)
    {
        await _ss.ResetPassword(reply);
        return new OkResult();
    }
}