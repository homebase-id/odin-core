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
    // POST /api/v2/contacts
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPost]
    [ProducesResponseType(typeof(UpsertContactResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> Upsert([FromBody] UpsertContactRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotNull(request.Content, nameof(request.Content));

        var result = await contactService.UpsertAsync(request.Content, request.VersionTag, WebOdinContext);

        if (!result.Success)
        {
            return Conflict(new ContactWriteConflict
            {
                VersionTag = result.VersionTag,
                Current = result.CurrentOnConflict
            });
        }

        return Ok(new UpsertContactResponse
        {
            UniqueId = result.UniqueId,
            VersionTag = result.VersionTag
        });
    }

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

        // Inline re-enrichment from the identity's profile (best-effort; merges contact data only).
        await contactEnrichmentService.EnrichAsync(id, WebOdinContext);
        return Accepted();
    }
}
