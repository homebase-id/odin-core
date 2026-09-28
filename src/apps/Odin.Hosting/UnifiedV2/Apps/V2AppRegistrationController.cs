#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Apps.V2;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Apps;

/// <summary>
/// V2 app registration: register an app together with the drives and circles it owns, and change it
/// afterwards (docs/app-registration-v2-plan-simplified.md).  Owner only.
/// </summary>
/// <remarks>
/// Built-in apps (and Mail) are refused; the identity manages those.  V1 registration at
/// <c>/api/owner/v1/appmanagement</c> is unchanged.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.AppRegistrations)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2AppRegistrationController(AppRegistrationV2Service appRegistrationV2Service) : OdinControllerBase
{
    /// <summary>Every registered app with the drives and circles it owns.</summary>
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<List<AppRegistrationV2>> GetAll()
    {
        return await appRegistrationV2Service.GetAllAsync(WebOdinContext);
    }

    /// <summary>One app with what it owns; 404 when not registered.</summary>
    [HttpGet("{appId:guid}")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<ActionResult<AppRegistrationV2>> Get([FromRoute] Guid appId)
    {
        var result = await appRegistrationV2Service.GetAsync(appId, WebOdinContext);
        return result == null ? NotFound() : result;
    }

    /// <summary>
    /// Dry run: every problem with the manifest and what applying it would change.  Writes nothing.
    /// </summary>
    [HttpPost("validate")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<AppRegistrationValidationResult> Validate([FromBody] AppManifestV2 manifest)
    {
        return await appRegistrationV2Service.ValidateAsync(manifest, WebOdinContext);
    }

    /// <summary>Registers the app, creating the drives and circles it owns.</summary>
    [HttpPost]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<AppRegistrationV2> Register([FromBody] AppManifestV2 manifest)
    {
        return await appRegistrationV2Service.RegisterAsync(manifest, WebOdinContext);
    }

    /// <summary>Adds owned drives and circles to an installed app in one call.</summary>
    [HttpPost("{appId:guid}/owned")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<AppRegistrationV2> AddOwned([FromRoute] Guid appId, [FromBody] AddOwnedResourcesRequest request)
    {
        return await appRegistrationV2Service.AddOwnedAsync(appId, request, WebOdinContext);
    }

    /// <summary>Replaces permission keys and explicit drive grants; owned drives are kept.</summary>
    [HttpPut("{appId:guid}/permissions")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> UpdatePermissions([FromRoute] Guid appId, [FromBody] UpdateAppPermissionsV2Request request)
    {
        await appRegistrationV2Service.UpdatePermissionsAsync(appId, request, WebOdinContext);
        return NoContent();
    }

    [HttpPut("{appId:guid}/authorized-circles")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> UpdateAuthorizedCircles([FromRoute] Guid appId,
        [FromBody] UpdateAuthorizedCirclesV2Request request)
    {
        await appRegistrationV2Service.UpdateAuthorizedCirclesAsync(appId, request, WebOdinContext);
        return NoContent();
    }

    [HttpPatch("{appId:guid}/owned-drives/{driveId:guid}")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> UpdateOwnedDrive([FromRoute] Guid appId, [FromRoute] Guid driveId,
        [FromBody] UpdateOwnedDriveRequest request)
    {
        await appRegistrationV2Service.UpdateOwnedDriveAsync(appId, driveId, request, WebOdinContext);
        return NoContent();
    }

    [HttpPut("{appId:guid}/owned-circles/{circleId:guid}")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> UpdateOwnedCircle([FromRoute] Guid appId, [FromRoute] Guid circleId,
        [FromBody] OwnedCircle circle)
    {
        await appRegistrationV2Service.UpdateOwnedCircleAsync(appId, circleId, circle, WebOdinContext);
        return NoContent();
    }

    [HttpDelete("{appId:guid}/owned-circles/{circleId:guid}")]
    [SwaggerOperation(Tags = [SwaggerInfo.AppRegistrations])]
    public async Task<IActionResult> DeleteOwnedCircle([FromRoute] Guid appId, [FromRoute] Guid circleId)
    {
        await appRegistrationV2Service.DeleteOwnedCircleAsync(appId, circleId, WebOdinContext);
        return NoContent();
    }
}
