using System.Threading.Tasks;
using Bitcoin.BIP39;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Services.Authentication.Owner;

namespace Odin.Hosting.Controllers.OwnerToken.Security;

[ApiController]
[Route(OwnerApiPathConstants.SecurityRecoveryV1)]
[AuthorizeValidOwnerToken]
public class SecurityVerificationController(OwnerAuthenticationService authService, OwnerSecretService ss) : OdinControllerBase
{
    [HttpPost("verify-password")]
    public async Task<IActionResult> VerifyPassword([FromBody] PasswordReply package)
    {
        WebOdinContext.Caller.AssertHasMasterKey();
        await authService.VerifyPasswordAsync(package, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-recovery-key")]
    public async Task<IActionResult> VerifyRecoveryKey([FromBody] VerifyRecoveryKeyRequest reply)
    {
        try
        {
            WebOdinContext.Caller.AssertHasMasterKey();
            await ss.VerifyRecoveryKeyAsync(reply, WebOdinContext);
        }
        catch (BIP39Exception e)
        {
            throw new OdinSecurityException("BIP39 failed", e);
        }

        return new OkResult();
    }
}