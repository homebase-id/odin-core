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
/// Set a contact's profile image. The client encrypts the image + thumbnails itself (AES under the
/// contact file's AES key — read from the file's <c>sharedSecretEncryptedKeyHeader</c> — and the
/// supplied <see cref="Iv"/>) and sends the <b>ciphertext</b>; the server stores it verbatim as the
/// <c>prfl_pic</c> payload. Addressed by uniqueId in the route; version-tag gated.
/// </summary>
public class SetContactImageRequest
{
    /// <summary>The contact's current version tag (optimistic concurrency).</summary>
    public Guid VersionTag { get; set; }

    /// <summary>MIME type of the image, e.g. <c>image/jpeg</c>.</summary>
    public string ContentType { get; set; }

    /// <summary>The 16-byte IV the client used to encrypt the image and all its thumbnails.</summary>
    public byte[] Iv { get; set; }

    /// <summary>Encrypted image bytes (base64 on the wire).</summary>
    public byte[] Content { get; set; }

    /// <summary>Client-generated thumbnails, each already encrypted under <see cref="Iv"/>.</summary>
    public List<ContactImageThumbnail> Thumbnails { get; set; } = new();
}

/// <summary>
/// A client-generated, client-encrypted thumbnail for the contact image (encrypted under the same IV
/// as the image payload).
/// </summary>
public class ContactImageThumbnail
{
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public string ContentType { get; set; }

    /// <summary>Encrypted thumbnail bytes (base64 on the wire).</summary>
    public byte[] Content { get; set; }
}

/// <summary>
/// Set (or replace) the calling app's per-app data blob on a contact. The server resolves the appId
/// from the caller's token and merges <b>only</b> that app's slot (<c>appData[appId]</c>) into the
/// contact JSON, leaving core fields and every other app's blob untouched. <see cref="Content"/> is a
/// small, opaque, app-authored JSON string (≤ <see cref="ContactService.AppDataBlobMaxBytes"/> bytes)
/// stored verbatim. Addressed by uniqueId in the route.
/// </summary>
public class SetContactAppDataRequest
{
    /// <summary>The app's opaque blob (verbatim JSON string). Over-cap writes are rejected.</summary>
    public string Content { get; set; }

    /// <summary>
    /// The caller's last-seen version tag (optimistic base). The namespaced merge applies the blob over
    /// the latest content and retries on a concurrent write, so a core/enrichment/other-app edit never
    /// surfaces as a conflict; same-app/other-device writes are last-write-wins.
    /// </summary>
    public Guid VersionTag { get; set; }
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
