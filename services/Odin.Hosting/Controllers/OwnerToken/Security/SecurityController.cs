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
public class SecurityController : Controller
{
    private readonly OdinContextAccessor _contextAccessor;
    private readonly RecoveryService _recoveryService;

    /// <summary />
    public SecurityController(OdinContextAccessor contextAccessor, RecoveryService recoveryService)
    {
        _contextAccessor = contextAccessor;
        _recoveryService = recoveryService;
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
    public async Task<byte[]> GetAccountRecoveryKey()
    {
        return await _recoveryService.GetKey();
    }
}