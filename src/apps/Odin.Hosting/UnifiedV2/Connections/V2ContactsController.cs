using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Apps;
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
    ContactEnrichmentService contactEnrichmentService,
    IAppRegistrationService appRegistrationService
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

    // PUT /api/v2/contacts/{uniqueId}/app-data  — set/replace the calling app's per-app blob (404/409 like update)
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPut("{uniqueId:guid}/app-data")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> SetAppData(Guid uniqueId, [FromBody] SetContactAppDataRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var appId = await ResolveCallingAppIdAsync();
        if (appId == null)
        {
            return BadRequest("app-data writes require an app token");
        }

        var result = await contactService.SetAppDataAsync(uniqueId, appId.Value, request.Content, request.VersionTag, WebOdinContext);
        return MapWrite(result);
    }

    // DELETE /api/v2/contacts/{uniqueId}/app-data?versionTag=...  — remove the calling app's per-app blob
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpDelete("{uniqueId:guid}/app-data")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> DeleteAppData(Guid uniqueId, [FromQuery] Guid versionTag)
    {
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var appId = await ResolveCallingAppIdAsync();
        if (appId == null)
        {
            return BadRequest("app-data writes require an app token");
        }

        var result = await contactService.DeleteAppDataAsync(uniqueId, appId.Value, versionTag, WebOdinContext);
        return MapWrite(result);
    }

    // PUT /api/v2/contacts/{uniqueId}/app-ext-data  — set/replace the calling app's bulk blob (appextdata payload)
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpPut("{uniqueId:guid}/app-ext-data")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> SetAppExtData(Guid uniqueId, [FromBody] SetContactAppExtDataRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var appId = await ResolveCallingAppIdAsync();
        if (appId == null)
        {
            return BadRequest("app-data writes require an app token");
        }

        var result = await contactService.SetAppExtDataAsync(uniqueId, appId.Value, request.Content, request.VersionTag, WebOdinContext);
        return MapWrite(result);
    }

    // DELETE /api/v2/contacts/{uniqueId}/app-ext-data?versionTag=...  — remove the calling app's bulk blob
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpDelete("{uniqueId:guid}/app-ext-data")]
    [ProducesResponseType(typeof(ContactWriteResponse), 200)]
    [ProducesResponseType(typeof(ContactWriteConflict), 409)]
    public async Task<IActionResult> DeleteAppExtData(Guid uniqueId, [FromQuery] Guid versionTag)
    {
        OdinValidationUtils.AssertNotEmptyGuid(uniqueId, nameof(uniqueId));

        var appId = await ResolveCallingAppIdAsync();
        if (appId == null)
        {
            return BadRequest("app-data writes require an app token");
        }

        var result = await contactService.DeleteAppExtDataAsync(uniqueId, appId.Value, versionTag, WebOdinContext);
        return MapWrite(result);
    }

    // Resolves the appId from the caller's token via the existing access-reg → AppClientRegistration
    // lookup. Null when the caller is not an app client (e.g. the owner console).
    private async Task<Guid?> ResolveCallingAppIdAsync()
    {
        var appId = await appRegistrationService.GetCallingAppIdAsync(WebOdinContext);
        return appId == null ? null : (Guid)appId;
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

    // GET /api/v2/contacts/attribute-types  — the canonical built-in profile attribute type registry
    // (literal GUIDs that match the data) so clients map a contact's social/profile attribute ids to a
    // known key without recomputing anything. Static built-ins; same OwnerOrApp auth as the rest.
    [SwaggerOperation(Tags = [SwaggerInfo.Contacts])]
    [HttpGet("attribute-types")]
    [ProducesResponseType(typeof(IReadOnlyList<ProfileAttributeType>), 200)]
    public IActionResult GetAttributeTypes()
    {
        return Ok(BuiltInProfileAttributes.All);
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
