using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Fluff;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Apps;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.AppManagement;

/// <summary>
/// The V2 twin of V1's <c>appmanagement</c> (<see cref="AppRegistrationController"/>), with its request and response
/// shapes. Owner console only, as V1 is: an app must not read or change registrations, its own grants included.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.AppManagement)]
[UnifiedV2Authorize(UnifiedPolicies.Owner)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2AppManagementController(
    IAppRegistrationService appRegistrationService,
    AppUninstallService appUninstallService) : OdinControllerBase
{
    [HttpGet("list")]
    public async Task<List<RedactedAppRegistration>> GetRegisteredApps()
    {
        return await appRegistrationService.GetRegisteredAppsAsync(WebOdinContext);
    }

    [HttpPost("app")]
    public async Task<RedactedAppRegistration> GetRegisteredApp([FromBody] GetAppRequest request)
    {
        return await appRegistrationService.GetAppRegistration(request.AppId, WebOdinContext);
    }

    [HttpPost("register/app")]
    public async Task<RedactedAppRegistration> RegisterApp([FromBody] AppRegistrationRequest appRegistration)
    {
        OdinValidationUtils.AssertNotNull(appRegistration, nameof(appRegistration));
        OdinValidationUtils.AssertIsTrue(appRegistration.IsValid(), "The app registration is invalid");

        return await appRegistrationService.RegisterAppAsync(appRegistration, WebOdinContext);
    }

    [HttpPost("register/update-app-permissions")]
    public async Task UpdateAppPermissions([FromBody] UpdateAppPermissionsRequest request)
    {
        await appRegistrationService.UpdateAppPermissionsAsync(request, WebOdinContext);
    }

    [HttpPost("register/update-authorized-circles")]
    public async Task UpdateAuthorizedCircles([FromBody] UpdateAuthorizedCirclesRequest request)
    {
        await appRegistrationService.UpdateAuthorizedCirclesAsync(request, WebOdinContext);
    }

    [HttpPost("revoke")]
    public async Task<NoResultResponse> RevokeApp([FromBody] GetAppRequest request)
    {
        await appRegistrationService.RevokeAppAsync(request.AppId, WebOdinContext);
        return new NoResultResponse(true);
    }

    [HttpPost("allow")]
    public async Task<NoResultResponse> AllowApp([FromBody] GetAppRequest request)
    {
        await appRegistrationService.RemoveAppRevocationAsync(request.AppId, WebOdinContext);
        return new NoResultResponse(true);
    }

    [HttpPost("uninstall")]
    public async Task<NoResultResponse> UninstallApp([FromBody] UninstallAppRequest request)
    {
        await appUninstallService.UninstallAsync(request.AppId, request.DeleteOwnedCirclesAndDrives, WebOdinContext);
        return new NoResultResponse(true);
    }

    [HttpPost("clients")]
    public async Task<List<RegisteredAppClientResponse>> GetRegisteredClients([FromBody] GetAppRequest request)
    {
        return await appRegistrationService.GetRegisteredClientsAsync(request.AppId, WebOdinContext);
    }

    [HttpPost("revoke-client")]
    public async Task RevokeClient([FromBody] GetAppClientRequest request)
    {
        await appRegistrationService.RevokeClientAsync(request.AccessRegistrationId, WebOdinContext);
    }

    [HttpPost("allow-client")]
    public async Task EnableClient([FromBody] GetAppClientRequest request)
    {
        await appRegistrationService.AllowClientAsync(request.AccessRegistrationId, WebOdinContext);
    }

    [HttpPost("delete-client")]
    public async Task DeleteClient([FromBody] GetAppClientRequest request)
    {
        await appRegistrationService.DeleteClientAsync(request.AccessRegistrationId, WebOdinContext);
    }
}
