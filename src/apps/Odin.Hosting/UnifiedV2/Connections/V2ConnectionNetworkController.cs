using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Membership.Connections.Verification;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Connections;

[ApiController]
[Route(UnifiedApiRouteConstants.Connections)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ConnectionNetworkController(
    CircleNetworkService circleNetwork,
    CircleMembershipService circleMembership,
    CircleNetworkVerificationService verificationService
) : OdinControllerBase
{
    [HttpPost("unblock")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Unblock a previously blocked identity")]
    public async Task<IActionResult> Unblock([FromBody] OdinIdRequest request)
    {
        await circleNetwork.UnblockAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("block")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Block an identity")]
    public async Task<IActionResult> Block([FromBody] OdinIdRequest request)
    {
        await circleNetwork.BlockAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("disconnect")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Disconnect from an identity")]
    public async Task<IActionResult> Disconnect([FromBody] OdinIdRequest request, [FromQuery] bool notifyRemote = true)
    {
        await circleNetwork.DisconnectAsync((OdinId)request.OdinId, WebOdinContext, notifyRemote);
        return Ok();
    }

    [HttpPost("confirm-connection")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Confirm a pending connection")]
    public async Task<IActionResult> ConfirmConnection([FromBody] OdinIdRequest request)
    {
        await circleNetwork.ConfirmConnectionAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("review")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections],
        Summary = "Record the owner's review of a connection: enroll the chosen circles and stamp ReviewedAt")]
    public async Task<IActionResult> MarkReviewed([FromBody] MarkConnectionReviewedRequest request)
    {
        await circleNetwork.MarkReviewedAsync((OdinId)request.OdinId, request.CircleIds, WebOdinContext);
        return Ok();
    }

    [HttpPost("review/clear")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Clear the owner's review of a connection")]
    public async Task<IActionResult> ClearReview([FromBody] OdinIdRequest request)
    {
        await circleNetwork.ClearReviewAsync((OdinId)request.OdinId, WebOdinContext);
        return Ok();
    }

    [HttpPost("verify-connection")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Verify connection status with an identity")]
    public async Task<IcrVerificationResult> VerifyConnection([FromBody] OdinIdRequest request)
    {
        var result = await verificationService.VerifyConnectionAsync(
            (OdinId)request.OdinId,
            HttpContext.RequestAborted,
            WebOdinContext
        );
        return result;
    }

    [HttpPost("troubleshooting-info")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get troubleshooting information for a connection")]
    public async Task<IcrTroubleshootingInfo> GetReconcilableStatus([FromBody] OdinIdRequest request)
    {
        var result = await circleNetwork.GetTroubleshootingInfoAsync((OdinId)request.OdinId, WebOdinContext);
        return result;
    }

    [HttpGet("status")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get connection status for an identity")]
    public async Task<RedactedIdentityConnectionRegistration> GetConnectionInfo([FromQuery] string odinId)
    {
        var result = await circleNetwork.GetIcrAsync(new OdinId(odinId), WebOdinContext);

        // Guests reach this route too; they get the identity, never the owner's judgments about it.
        if (!CallerIsOwnerSideViewer)
        {
            return result?.RedactedForExternalViewer();
        }

        var redacted = result?.Redacted();
        await circleNetwork.PopulateAwaitingAppNamesAsync([redacted], WebOdinContext);
        return redacted;
    }

    [HttpGet("connected")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get list of connected identities")]
    public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetConnectedIdentities(int count, string cursor)
    {
        var result = await circleNetwork.GetConnectedIdentitiesAsync(count, cursor, WebOdinContext);
        var ownerSide = CallerIsOwnerSideViewer;

        var results = result.Results
            .Select(p => ownerSide ? p.Redacted() : p.RedactedForExternalViewer())
            .ToList();

        if (ownerSide)
        {
            // One pass for the page: the lookups are memoised across it, and most connections have
            // nothing awaiting, so this costs nothing for them.
            await circleNetwork.PopulateAwaitingAppNamesAsync(results, WebOdinContext);
        }

        return new CursoredResult<RedactedIdentityConnectionRegistration>()
        {
            Cursor = result.Cursor,
            Results = results
        };
    }

    [HttpGet("blocked")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get list of blocked identities")]
    public async Task<CursoredResult<RedactedIdentityConnectionRegistration>> GetBlockedProfiles(int count, string cursor)
    {
        var result = await circleNetwork.GetBlockedProfilesAsync(count, cursor, WebOdinContext);
        return new CursoredResult<RedactedIdentityConnectionRegistration>()
        {
            Cursor = result.Cursor,
            Results = result.Results.Select(p => p.Redacted()).ToList()
        };
    }

    [HttpGet("circles")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Get members of a circle")]
    public async Task<IEnumerable<OdinId>> GetCircleMembers(Guid circleId)
    {
        var result = await circleNetwork.GetCircleMembersAsync(circleId, WebOdinContext);
        return result;
    }

    [HttpPost("enrollments/process")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections],
        Summary = "Complete the pending circle enrollments this caller is able to complete")]
    public async Task<PendingEnrollmentProcessingResult> ProcessPendingEnrollments()
    {
        var (connectionsProcessed, enrollmentsCompleted) =
            await circleNetwork.ProcessPendingEnrollmentsForAppAsync(WebOdinContext);

        return new PendingEnrollmentProcessingResult
        {
            ConnectionsProcessed = connectionsProcessed,
            EnrollmentsCompleted = enrollmentsCompleted
        };
    }

    /// <summary>
    /// Per circle owned by an app, the connections that could be added to it but are not in it.
    /// </summary>
    /// <remarks>
    /// Answered here rather than left to each app to derive from the connection list, because the
    /// eligibility rule is subtle -- GrantOn semantics, auto-connected exclusion, entries already
    /// deposited or queued -- and reimplementations would drift from the server's definition of who
    /// qualifies.  An app may ask only about itself; the console may ask about any app.
    /// </remarks>
    [HttpGet("circles/enrollment-candidates")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections],
        Summary = "Connections eligible for an app's circles that are not in them yet")]
    public async Task<IEnumerable<CircleEnrollmentCandidates>> GetEnrollmentCandidates([FromQuery] Guid appId)
    {
        OdinValidationUtils.AssertNotEmptyGuid(appId, nameof(appId));
        return await circleNetwork.GetEnrollmentCandidatesForAppAsync(appId, WebOdinContext);
    }

    /// <summary>
    /// Adds several identities to one circle in a single call.
    /// </summary>
    /// <remarks>
    /// Open to the owning app, not only the console.  An app enrolling into a Read circle produces
    /// deposits rather than membership -- it cannot reach the connection's Peer Key -- so the owner
    /// doing it is faster, but the app doing it is not wrong: the work is recorded and completes
    /// when that key is next in scope.
    /// </remarks>
    [HttpPost("circles/add-many")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections],
        Summary = "Add several identities to one circle")]
    public async Task<EnrollmentResult> GrantCircleToMany([FromBody] AddManyCircleMembershipRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(request.CircleId, nameof(request.CircleId));

        var odinIds = (request.OdinIds ?? []).Select(id => new OdinId(id)).ToList();
        return await circleNetwork.EnrollManyInCircleAsync(new GuidId(request.CircleId), odinIds, WebOdinContext);
    }

    [HttpGet("circles/pending")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections],
        Summary = "Get identities whose grant for a circle is deposited but not yet in effect")]
    public async Task<IEnumerable<PendingCircleMember>> GetPendingCircleMembers(Guid circleId)
    {
        return await circleNetwork.GetPendingCircleMembersAsync(circleId, WebOdinContext);
    }

    [HttpGet("circles/with-members")]
    [UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "List all circles and their members")]
    public async Task<IEnumerable<CircleWithMembers>> GetCirclesWithMembers([FromQuery] bool includeSystemCircle = true)
    {
        var circles = await circleMembership.GetCircleDefinitions(includeSystemCircle, WebOdinContext);

        // One pass for every circle, rather than a connection scan each time through the loop below.
        var pending = await circleNetwork.GetAllPendingCircleMembersAsync(WebOdinContext);

        var result = new List<CircleWithMembers>();
        foreach (var circle in circles)
        {
            // Sequential by design: the request scope holds a single DB connection (not safe for
            // concurrent use), so we don't fan these out.
            var members = await circleNetwork.GetCircleMembersAsync(circle.Id, WebOdinContext);
            result.Add(new CircleWithMembers
            {
                Circle = circle.Redacted(),
                Members = members.ToList(),
                PendingMembers = pending.TryGetValue(circle.Id.Value, out var p) ? p : []
            });
        }

        return result;
    }

    [HttpPost("circles/add")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Add an identity to a circle")]
    public async Task<IActionResult> GrantCircle([FromBody] AddCircleMembershipRequest request)
    {
        await circleNetwork.GrantCircleAsync(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
        return Ok();
    }

    [HttpPost("circles/revoke")]
    [SwaggerOperation(Tags = [SwaggerInfo.Connections], Summary = "Remove an identity from a circle")]
    public async Task<IActionResult> RevokeCircle([FromBody] RevokeCircleMembershipRequest request)
    {
        await circleNetwork.RevokeCircleAccessAsync(request.CircleId, new OdinId(request.OdinId), WebOdinContext);
        return Ok();
    }
}

/// <summary>A circle definition (permission set redacted) together with its member identities.</summary>
/// <summary>What a call to process pending enrollments managed to finish.</summary>
public class PendingEnrollmentProcessingResult
{
    public int ConnectionsProcessed { get; set; }
    public int EnrollmentsCompleted { get; set; }
}

public class CircleWithMembers
{
    public RedactedCircleDefinition Circle { get; set; }
    public List<OdinId> Members { get; set; }

    /// <summary>
    /// Identities asked into this circle by an app whose grant has not converted yet.  Deliberately a
    /// sibling of <see cref="Members"/> rather than part of it: they hold nothing yet, and a client that
    /// does not know about this field keeps behaving exactly as it did.
    /// </summary>
    public List<PendingCircleMember> PendingMembers { get; set; }
}