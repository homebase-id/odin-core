using System;
using System.Collections.Generic;
using Odin.Services.Apps;

namespace Odin.Services.Contacts;

/// <summary>
/// Create a contact. The client sends plaintext <see cref="Content"/> over the normal shared-secret
/// transport; the server encrypts it at rest with a per-file key header and assigns the unique id
/// (deterministic from <c>Content.OdinId</c> if present, else random) returned in
/// <see cref="ContactWriteResponse"/>. A create that collides with an existing contact is rejected
/// with <b>409 Conflict</b> — update it instead.
/// </summary>
public class CreateContactRequest
{
    public ContactContent Content { get; set; }
}

/// <summary>
/// Update an existing contact addressed by its unique id (in the route). Optimistic concurrency:
/// <see cref="VersionTag"/> is required and the write is rejected with <b>409 Conflict</b> (carrying
/// <see cref="ContactWriteConflict"/>) if it is stale, or <b>404</b> if no such contact exists.
/// </summary>
public class UpdateContactRequest
{
    public ContactContent Content { get; set; }

    /// <summary>The version tag the client last read; must match the stored contact's current tag.</summary>
    public Guid VersionTag { get; set; }
}

/// <summary>
/// Set a contact's profile image. The client sends <b>plaintext</b> image + thumbnail bytes over the
/// shared-secret transport (the server is the at-rest encryption authority for the contact, so it
/// encrypts them under the file key). Addressed by uniqueId in the route; version-tag gated.
/// </summary>
public class SetContactImageRequest
{
    /// <summary>The contact's current version tag (optimistic concurrency).</summary>
    public Guid VersionTag { get; set; }

    /// <summary>MIME type of the image, e.g. <c>image/jpeg</c>.</summary>
    public string ContentType { get; set; }

    /// <summary>Plaintext image bytes (serialized as base64).</summary>
    public byte[] Content { get; set; }

    /// <summary>Optional client-generated thumbnails (plaintext); the server encrypts them at rest.</summary>
    public List<ContactImageThumbnail> Thumbnails { get; set; } = new();
}

/// <summary>
/// A client-generated thumbnail for the contact image. Plaintext bytes; the server encrypts at rest
/// under the same IV as the image payload.
/// </summary>
public class ContactImageThumbnail
{
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public string ContentType { get; set; }
    public byte[] Content { get; set; }
}

/// <summary>
/// Returned on a successful create or update.
/// </summary>
public class ContactWriteResponse
{
    /// <summary>The unique id the contact is keyed on (deterministic from odinId, else random).</summary>
    public Guid UniqueId { get; set; }

    /// <summary>The new version tag to carry on the next update.</summary>
    public Guid VersionTag { get; set; }
}

/// <summary>
/// Body returned with <b>409 Conflict</b> — a create against an existing contact, or an update with a
/// stale version tag — so the client can re-fetch, re-apply its edit, and retry.
/// </summary>
public class ContactWriteConflict
{
    /// <summary>The current (authoritative) version tag of the stored contact.</summary>
    public Guid VersionTag { get; set; }

    /// <summary>The current stored contact, in the same shared-secret-encrypted shape as any drive read.</summary>
    public SharedSecretEncryptedFileHeader Current { get; set; }
}

/// <summary>
/// Outcome of a <see cref="ContactService"/> create/update.
/// </summary>
public enum ContactWriteOutcome
{
    Created,
    Updated,

    /// <summary>Create collided with an existing contact (deterministic odinId key). → 409.</summary>
    AlreadyExists,

    /// <summary>Update target does not exist. → 404.</summary>
    NotFound,

    /// <summary>Update version tag was stale. → 409.</summary>
    VersionConflict
}

/// <summary>
/// Result of a <see cref="ContactService"/> create/update: the outcome plus the contact's id/tag and,
/// for the conflict outcomes, the current header for the controller to surface as 409.
/// </summary>
public class ContactWriteResult
{
    public ContactWriteOutcome Outcome { get; init; }
    public Guid UniqueId { get; init; }
    public Guid VersionTag { get; init; }

    /// <summary>Set only for <see cref="ContactWriteOutcome.AlreadyExists"/> / <see cref="ContactWriteOutcome.VersionConflict"/>.</summary>
    public SharedSecretEncryptedFileHeader CurrentOnConflict { get; init; }
}
