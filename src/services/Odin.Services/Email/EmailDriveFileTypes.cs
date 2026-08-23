using System;

namespace Odin.Services.Email;

/// <summary>
/// The file shapes stored on the Email app's drive.
///
/// Mirrored by chat-kmp <c>homebase-core/.../ui/screens/email/model/EmailFileTypes.kt</c> —
/// change one, change both.
///
/// Every type here has exactly ONE writer, and that is deliberate: two writers on the same file
/// type means a read-modify-write race and a conflict loop. The server owns the key material and
/// the pointer to the current key; the client owns its own record of the credentials it asked
/// for.
/// </summary>
public static class EmailDriveFileTypes
{
    /// <summary>
    /// One OpenPGP keyring, written by the SERVER. Append-only and never deleted: mail received
    /// under an older key stays decryptable only while that key survives.
    /// </summary>
    public const int KeyMaterial = 7301;

    /// <summary>
    /// Which keyring is current, written by the SERVER. A singleton, so "current" never depends
    /// on file ordering or timestamps.
    /// </summary>
    public const int CurrentKeyPointer = 7302;

    /// <summary>
    /// One issued mail-client credential, written by the CLIENT. The list of these IS the record
    /// of what was issued — there is no server-side list API.
    /// </summary>
    public const int AppPasswordCredential = 7304;

    /// <summary>The fixed unique id of the current-key pointer singleton.</summary>
    public static readonly Guid CurrentKeyPointerUniqueId = Guid.Parse("7e0c1d54-6b3a-4f28-9a71-c50f2d84b3e6");
}

/// <summary>
/// The content of a <see cref="EmailDriveFileTypes.KeyMaterial"/> file: an OpenPGP keyring, both
/// halves. This is the identity's only copy of the private half — it is generated on the server
/// and written straight here, and nothing else keeps it.
/// </summary>
public class EmailKeyMaterialContent
{
    public string SecretKeyArmored { get; init; } = "";
    public string PublicCertificateArmored { get; init; } = "";
    public string FingerprintHex { get; init; } = "";

    /// <summary>The address the key was bound to, i.e. the OpenPGP user id.</summary>
    public string UserId { get; init; } = "";

    public long CreatedUtc { get; init; }
}

/// <summary>Points at the keyring currently being published and encrypted to.</summary>
public class EmailCurrentKeyContent
{
    public Guid KeyFileUniqueId { get; init; }
    public string FingerprintHex { get; init; } = "";
    public long UpdatedUtc { get; init; }
}
