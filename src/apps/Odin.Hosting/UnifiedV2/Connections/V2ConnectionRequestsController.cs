using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Connections;

[ApiController]
[Route(UnifiedApiRouteConstants.Connections)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrAppOrGuest)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ConnectionRequestsController(
    CircleNetworkRequestService circleNetworkRequestService
) : OdinControllerBase
{
    // GET /requests?type=incoming|outgoing&pageNumber=1&pageSize=20
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests(
        [FromQuery] string type,
        [FromQuery] int pageNumber,
        [FromQuery] int pageSize)
    {
        var pageOptions = new PageOptions(pageNumber, pageSize);

        if (type?.ToLower() == "incoming")
        {
            var result = await circleNetworkRequestService
                .GetPendingRequestsAsync(pageOptions, WebOdinContext);

            return Ok(result);
        }

        if (type?.ToLower() == "outgoing")
        {
            var result = await circleNetworkRequestService
                .GetSentRequestsAsync(pageOptions, WebOdinContext);

            var mapped = result.Results
                .Select(r => ConnectionRequestResponse
                    .FromConnectionRequest(r, ConnectionRequestDirection.Outgoing))
                .ToList();

            return Ok(new PagedResult<ConnectionRequestResponse>(
                result.Request,
                result.TotalPages,
                mapped));
        }

        return BadRequest("Invalid type. Use incoming or outgoing.");
    }

    // GET /requests/incoming/{senderId}
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpGet("requests/incoming/{senderId}")]
    public async Task<IActionResult> GetIncomingRequest(string senderId)
    {
        AssertIsValidOdinId(senderId, out var id);

        var result = await circleNetworkRequestService
            .GetPendingRequestAsync(id, WebOdinContext);

        if (result == null)
            return NotFound();

        return Ok(ConnectionRequestResponse
            .FromConnectionRequest(result, ConnectionRequestDirection.Incoming));
    }

    // GET /requests/outgoing/{recipientId}
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpGet("requests/outgoing/{recipientId}")]
    public async Task<IActionResult> GetOutgoingRequest(string recipientId)
    {
        AssertIsValidOdinId(recipientId, out var id);

        var result = await circleNetworkRequestService
            .GetSentRequestAsync(id, WebOdinContext);

        if (result == null)
            return NotFound();

        return Ok(ConnectionRequestResponse
            .FromConnectionRequest(result, ConnectionRequestDirection.Outgoing));
    }

    // POST /requests
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpPost("requests")]
    public async Task<IActionResult> SendConnectionRequest(
        [FromBody] ConnectionRequestHeader requestHeader)
    {
        OdinValidationUtils.AssertNotNull(requestHeader, nameof(requestHeader));
        OdinValidationUtils.AssertIsTrue(requestHeader.Id != Guid.Empty, "Invalid Id");
        OdinValidationUtils.AssertIsValidOdinId(requestHeader.Recipient, out _);

        await circleNetworkRequestService
            .SendConnectionRequestAsync(requestHeader, HttpContext.RequestAborted, WebOdinContext);

        return CreatedAtAction(nameof(GetOutgoingRequest),
            new { recipientId = requestHeader.Recipient },
            null);
    }

    // PUT /requests/incoming/{senderId}
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpPut("requests/incoming/{senderId}")]
    public async Task<IActionResult> AcceptIncomingRequest(string senderId)
    {
        AssertIsValidOdinId(senderId, out var id);

        var header = new AcceptRequestHeader { Sender = id };
        header.Validate();

        await circleNetworkRequestService
            .AcceptConnectionRequestAsync(header, false, WebOdinContext);

        return NoContent();
    }

    // DELETE /requests/incoming/{senderId}
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpDelete("requests/incoming/{senderId}")]
    public async Task<IActionResult> RejectIncomingRequest(string senderId)
    {
        AssertIsValidOdinId(senderId, out var id);

        await circleNetworkRequestService
            .DeletePendingRequest(id, WebOdinContext);

        return NoContent();
    }

    // DELETE /requests/outgoing/{recipientId}
    [SwaggerOperation(Tags = [SwaggerInfo.Connections])]
    [HttpDelete("requests/outgoing/{recipientId}")]
    public async Task<IActionResult> CancelOutgoingRequest(string recipientId)
    {
        AssertIsValidOdinId(recipientId, out var id);

        await circleNetworkRequestService
            .DeleteSentRequest(id, WebOdinContext);

        return NoContent();
    }
}
