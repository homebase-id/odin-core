using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Profile;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Profile;

/// <summary>
/// Server-side writes of built-in profile attributes to the ProfileDrive. Reads stay client-side (clients
/// query attributes as files from the ProfileDrive), so this controller exposes only the write surface.
/// All writes funnel through <see cref="ProfileAttributeService"/>, which requires the ManageProfile
/// permission.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Profile)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ProfileController(
    ProfileAttributeService profileAttributeService
) : OdinControllerBase
{
    // PUT /api/v2/profile/attributes  — create or edit a built-in profile attribute (409 on a stale
    // version tag when editing).
    [SwaggerOperation(Tags = [SwaggerInfo.Profile])]
    [HttpPut("attributes")]
    [ProducesResponseType(typeof(ProfileAttributeWriteResponse), 200)]
    [ProducesResponseType(typeof(ProfileAttributeWriteConflict), 409)]
    public async Task<IActionResult> SetAttribute([FromBody] SetProfileAttributeRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        var result = await profileAttributeService.SetAttributeAsync(request, WebOdinContext);
        if (result.Outcome == ProfileAttributeWriteOutcome.VersionConflict)
        {
            return Conflict(new ProfileAttributeWriteConflict { Id = result.Id, VersionTag = result.VersionTag });
        }

        return Ok(new ProfileAttributeWriteResponse { Id = result.Id, VersionTag = result.VersionTag });
    }

    // PUT /api/v2/profile/attributes/photo — create or edit the Photo attribute (409 on a stale version
    // tag when editing). Plain JSON body like SetAttribute above, not multipart: profile photos are capped
    // small (a 200x200 rendition is ~10-20KB; nothing here should approach six figures of bytes), so the
    // ~33% base64 overhead is negligible and not worth the multipart parsing complexity. Unlike SetAttribute,
    // the image + its pre-generated thumbnails ride as a payload (see SetPhotoAttributeRequest); the server
    // does not resize images, so the caller supplies every rendition it wants stored, plaintext — the server
    // encrypts at rest itself based on Visibility (matches SetAttribute, not SetContactImageRequest, which
    // requires client-side pre-encryption).
    [SwaggerOperation(Tags = [SwaggerInfo.Profile])]
    [HttpPut("attributes/photo")]
    [ProducesResponseType(typeof(ProfileAttributeWriteResponse), 200)]
    [ProducesResponseType(typeof(ProfileAttributeWriteConflict), 409)]
    public async Task<IActionResult> SetPhotoAttribute([FromBody] SetPhotoAttributeRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        var result = await profileAttributeService.SetPhotoAttributeAsync(request, WebOdinContext);
        if (result.Outcome == ProfileAttributeWriteOutcome.VersionConflict)
        {
            return Conflict(new ProfileAttributeWriteConflict { Id = result.Id, VersionTag = result.VersionTag });
        }

        return Ok(new ProfileAttributeWriteResponse { Id = result.Id, VersionTag = result.VersionTag });
    }

    // DELETE /api/v2/profile/attributes/{id}?versionTag=...  — delete an attribute (404 if missing,
    // 409 on a stale version tag).
    [SwaggerOperation(Tags = [SwaggerInfo.Profile])]
    [HttpDelete("attributes/{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ProfileAttributeWriteConflict), 409)]
    public async Task<IActionResult> DeleteAttribute(Guid id, [FromQuery] Guid versionTag)
    {
        OdinValidationUtils.AssertNotEmptyGuid(id, nameof(id));

        var result = await profileAttributeService.DeleteAttributeAsync(id, versionTag, WebOdinContext);
        return result.Outcome switch
        {
            ProfileAttributeDeleteOutcome.NotFound => NotFound(),
            ProfileAttributeDeleteOutcome.VersionConflict =>
                Conflict(new ProfileAttributeWriteConflict { Id = id, VersionTag = result.VersionTag }),
            _ => NoContent()
        };
    }
}

public sealed class ProfileAttributeWriteResponse
{
    public Guid Id { get; init; }
    public Guid VersionTag { get; init; }
}

public sealed class ProfileAttributeWriteConflict
{
    public Guid Id { get; init; }
    public Guid VersionTag { get; init; }
}
