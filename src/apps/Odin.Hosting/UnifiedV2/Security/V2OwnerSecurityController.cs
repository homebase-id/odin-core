using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authentication.Owner;
using Odin.Services.Security;
using Odin.Services.Security.PasswordRecovery.RecoveryPhrase;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Security;

/// <summary>
/// The recovery phrase, the password change and account deletion: V2 of <c>OwnerSecurityController</c>, same services,
/// owner console only. V1's <c>security/context</c> already has its V2 twin at <c>auth/context</c>.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Security)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2OwnerSecurityController(
    PasswordKeyRecoveryService recoveryService,
    OwnerSecretService secretService,
    OwnerAuthenticationService ownerAuthenticationService) : OdinControllerBase
{
    [HttpPost("confirm-stored-recovery-key")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task ConfirmStoredRecoveryKey()
    {
        await recoveryService.ConfirmInitialRecoveryKeyStorage(WebOdinContext);
    }

    [HttpGet("recovery-key")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<RecoveryKeyResult> GetAccountRecoveryKey()
    {
        return await recoveryService.GetRecoveryKeyAsync(byPassWaitingPeriod: false, WebOdinContext);
    }

    [HttpPost("request-recovery-key")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<ActionResult<RequestRecoveryKeyResult>> RequestRecoveryKey()
    {
        return await recoveryService.RequestRecoveryKey(WebOdinContext);
    }

    [HttpPost("reset-password")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await secretService.ResetPasswordAsync(request, WebOdinContext);
        return new OkResult();
    }

    [HttpGet("account-status")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<AccountStatusResponse> GetAccountStatus()
    {
        return await ownerAuthenticationService.GetAccountStatusAsync(WebOdinContext);
    }

    [HttpPost("delete-account")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        await ownerAuthenticationService.MarkForDeletionAsync(request.CurrentAuthenticationPasswordReply, WebOdinContext);
        return new OkResult();
    }

    [HttpPost("undelete-account")]
    [SwaggerOperation(Tags = [SwaggerInfo.OwnerSecurity])]
    public async Task<IActionResult> UndeleteAccount([FromBody] DeleteAccountRequest request)
    {
        await ownerAuthenticationService.UnmarkForDeletionAsync(request.CurrentAuthenticationPasswordReply, WebOdinContext);
        return new OkResult();
    }
}
