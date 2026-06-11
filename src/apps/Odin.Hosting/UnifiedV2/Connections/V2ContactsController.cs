using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Contacts;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Connections;

/// <summary>
/// Server-side contact writes against the contact drive. Reads stay client-side (clients read contacts
/// as plain files from the contact drive), so this controller exposes only the write surface.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Contacts)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ContactsController(
    ContactService contactService,
    ContactEnrichmentService contactEnrichmentService
) : OdinControllerBase
{
    // POST /api/v2/contacts  — create (409 if it already exists; update it instead)
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPost]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> Create([FromBody] CreateContactRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Content, nameof(request.Content));

        var result = await contactService.CreateAsync(request.Content, WebOdinContext);
        if (result.Outcome == ContactWriteOutcome.AlreadyExists)
        {
            return Conflict(new ContactWriteConflict { VersionTag = result.VersionTag, Current = result.CurrentOnConflict });
        }

        return Ok(new ContactWriteResponse { UniqueId = result.UniqueId, VersionTag = result.VersionTag });
    }

    // PUT /api/v2/contacts/{uniqueId}  — update (404 if missing, 409 on stale version tag)
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPut("{uniqueId:guid}")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> Update(Guid uniqueId, [FromBody] UpdateContactRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Content, nameof(request.Content));
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var result = await contactService.UpdateAsync(uniqueId, request.Content, request.VersionTag, WebOdinContext);
        return MapWrite(result);
    }

    // PUT /api/v2/contacts/{uniqueId}/image  — set/replace the contact's profile image (404/409 like update)
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPut("{uniqueId:guid}/image")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> SetImage(Guid uniqueId, [FromBody] SetContactImageRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var result = await contactService.SetImageAsync(uniqueId, request, WebOdinContext);
        return MapWrite(result);
    }

    // DELETE /api/v2/contacts/{uniqueId}/image?versionTag=...  — remove the contact's profile image
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpDelete("{uniqueId:guid}/image")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> DeleteImage(Guid uniqueId, [FromQuery] Guid versionTag)
    {
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var result = await contactService.DeleteImageAsync(uniqueId, versionTag, WebOdinContext);
        return MapWrite(result);
    }

    private IActionResult MapWrite(ContactWriteResult result) => result.Outcome switch
    {
        ContactWriteOutcome.NotFound => NotFound(),
        ContactWriteOutcome.VersionConflict =>
            Conflict(new ContactWriteConflict { VersionTag = result.VersionTag, Current = result.CurrentOnConflict }),
        _ => Ok(new ContactWriteResponse { UniqueId = result.UniqueId, VersionTag = result.VersionTag })
    };

    // DELETE /api/v2/contacts/{uniqueId}
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpDelete("{uniqueId:guid}")]
    public async Task<IActionResult> Delete(Guid uniqueId)
    {
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var deleted = await contactService.DeleteByUniqueIdAsync(uniqueId, WebOdinContext);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    // POST /api/v2/contacts/sync/{odinId}
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPost("sync/{odinId}")]
    public async Task<IActionResult> Sync(string odinId)
    {
        AssertIsValidOdinId(odinId, out var id);

        // Phase 1: enrichment is client-triggered. Ensure the contact exists, then enrich it inline
        // from the identity's profile (best-effort; merges contact data only).
        await contactService.EnsureExistsAsync(id, WebOdinContext);
        await contactEnrichmentService.EnrichAsync(id, WebOdinContext);
        return Accepted();
    }
}
