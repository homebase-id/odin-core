using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Connections;

/// <summary>
/// The V2 twin of V1's <c>circles/definitions</c> and <c>circles/membership</c>, with their request and response shapes.
/// V1 mounts these on the owner and on apps, so the routes admit both and the services decide, as there: reading needs
/// ReadCircleMembership, and create and update need the master key, so an app gets as far as the service and is refused.
/// Moving a circle between apps was never on the app controller and is owner console only here.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Connections + "/circles")]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2CircleDefinitionController(
    CircleNetworkService circleNetwork,
    CircleMembershipService circleMembership) : OdinControllerBase
{
    /// <param name="includeSystemCircle">Ignored, as on V1. Kept so existing callers still bind; the system circles retired (#1809).</param>
    [HttpGet("definitions/list")]
    public async Task<IEnumerable<CircleDefinition>> GetCircleDefinitions(bool includeSystemCircle)
    {
        return await circleMembership.GetCircleDefinitions(WebOdinContext);
    }

    [HttpPost("definitions/get")]
    public async Task<CircleDefinition> GetCircle([FromBody] Guid id)
    {
        return await circleMembership.GetCircleAsync(id, WebOdinContext);
    }

    [HttpPost("definitions/create")]
    public async Task<bool> CreateCircle([FromBody] CreateCircleRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNullOrEmpty(request.Name, nameof(request.Name));
        OdinValidationUtils.AssertNotEmptyGuid(request.Id, nameof(request.Id));

        await circleMembership.CreateCircleDefinitionAsync(request, WebOdinContext);
        return true;
    }

    [HttpPost("definitions/update")]
    public async Task<bool> UpdateCircle([FromBody] CircleDefinition circleDefinition)
    {
        await circleNetwork.UpdateCircleDefinitionAsync(circleDefinition, WebOdinContext);
        return true;
    }

    /// <summary>Hands a circle that belongs to no app to one that does exist. One way.</summary>
    [HttpPost("definitions/set-owner")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    public async Task<bool> SetCircleOwningApp([FromBody] SetCircleOwningAppRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(request.CircleId, nameof(request.CircleId));
        OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

        await circleNetwork.SetCircleOwningAppAsync(new GuidId(request.CircleId), request.AppId, WebOdinContext);
        return true;
    }

    /// <summary>Moves a circle from the app that owns it to another: the escape hatch out of set-owner's one-way rule.</summary>
    [HttpPost("definitions/reassign-owner")]
    [UnifiedV2Authorize(UnifiedPolicies.Owner)]
    public async Task<ReassignCircleOwningAppResult> ReassignCircleOwningApp([FromBody] SetCircleOwningAppRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(request.CircleId, nameof(request.CircleId));
        OdinValidationUtils.AssertNotEmptyGuid(request.AppId, nameof(request.AppId));

        var repointed = await circleNetwork.ReassignCircleOwningAppAsync(new GuidId(request.CircleId), request.AppId, WebOdinContext);
        return new ReassignCircleOwningAppResult { EnrollmentsRepointed = repointed };
    }

    /// <summary>Everyone in a circle, identities and YouAuth domains alike, where <c>connections/circles</c> lists identities only.</summary>
    [HttpPost("membership/list")]
    public async Task<List<CircleDomainResult>> GetDomainsInCircle([FromBody] GetCircleMembersRequest request)
    {
        return await circleMembership.GetDomainsInCircleAsync(request.CircleId, WebOdinContext);
    }
}
