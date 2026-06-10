using System;
using Odin.Services.Apps;

namespace Odin.Services.Contacts;

/// <summary>
/// Upsert (create-or-update) a contact. The client sends plaintext <see cref="Content"/> over the
/// normal shared-secret transport; the server encrypts it at rest with a per-file key header.
/// </summary>
public class UpsertContactRequest
{
    public ContactContent Content { get; set; }

    /// <summary>
    /// The version tag the client last read. Required when updating an existing contact; the write is
    /// rejected with <b>409 Conflict</b> (carrying <see cref="ContactWriteConflict"/>) if it is stale.
    /// Omit (null) when creating.
    /// </summary>
    public Guid? VersionTag { get; set; }
}

/// <summary>
/// Returned on a successful upsert.
/// </summary>
public class UpsertContactResponse
{
    /// <summary>The unique id the contact was keyed on (deterministic from odinId, else random).</summary>
    public Guid UniqueId { get; set; }

    /// <summary>The new version tag to carry on the next update.</summary>
    public Guid VersionTag { get; set; }
}

/// <summary>
/// Body returned with <b>409 Conflict</b> when an update is attempted with a stale version tag, so the
/// client can re-fetch, re-apply its edit, and retry.
/// </summary>
public class ContactWriteConflict
{
    /// <summary>The current (authoritative) version tag of the stored contact.</summary>
    public Guid VersionTag { get; set; }

    /// <summary>The current stored contact, in the same shared-secret-encrypted shape as any drive read.</summary>
    public SharedSecretEncryptedFileHeader Current { get; set; }
}

/// <summary>
/// Outcome of <see cref="ContactService.UpsertAsync"/>: either a success or an optimistic-concurrency
/// conflict carrying the current header for the controller to surface as 409.
/// </summary>
public class ContactUpsertResult
{
    public bool Success { get; init; }
    public Guid UniqueId { get; init; }
    public Guid VersionTag { get; init; }

    /// <summary>Set only when <see cref="Success"/> is false.</summary>
    public SharedSecretEncryptedFileHeader CurrentOnConflict { get; init; }
}
