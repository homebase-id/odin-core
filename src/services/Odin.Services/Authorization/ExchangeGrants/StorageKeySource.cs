#nullable enable

using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Authorization.ExchangeGrants;

/// <summary>
/// How grant minting reaches a drive's storage key: minting a readable drive grant
/// re-encrypts the drive's storage key under the new grant's key store key, and each
/// caller reaches that storage key a different way — or, for legacy weak flows,
/// deliberately not at all. Every mint site declares its source explicitly; there is
/// no implicit "null master key means keyless grant" path.
/// </summary>
public interface IStorageKeySource
{
    /// <summary>
    /// Returns the drive's storage key (caller wipes it), or null when this source
    /// deliberately mints keyless grants. Throws <see cref="OdinSecurityException"/>
    /// when the caller should hold the key but cannot reach it.
    /// </summary>
    SensitiveByteArray? GetStorageKey(StorageDrive drive);
}

public static class StorageKeySource
{
    /// <summary>
    /// For connection-request grants (send and accept), where the caller may be an app or an
    /// introduction with no master key: source via the master key when present, otherwise via the
    /// caller's own drive access, minting keyless only for drives the caller cannot read.
    /// </summary>
    public static IStorageKeySource FromMasterKeyOrCaller(SensitiveByteArray? masterKey, IOdinContext odinContext)
    {
        return masterKey == null
            ? new PermissionContextOrNoStorageKeySource(odinContext)
            : new MasterKeyStorageKeySource(masterKey);
    }
}

/// <summary>Owner path: the drive's canonical root, unwrapped with the master key.</summary>
public sealed class MasterKeyStorageKeySource(SensitiveByteArray masterKey) : IStorageKeySource
{
    public SensitiveByteArray GetStorageKey(StorageDrive drive)
    {
        return drive.MasterKeyEncryptedStorageKey.DecryptKeyClone(masterKey);
    }
}

/// <summary>
/// Caller-scoped path: the storage key comes from the caller's own permission context
/// (e.g. an app: client auth token → key store key → drive grant → storage key). Reaches
/// only drives the caller can already read; a grant on any other drive throws rather than
/// silently minting a member who is "in the circle" but can read nothing.
/// </summary>
public sealed class PermissionContextStorageKeySource(IOdinContext odinContext) : IStorageKeySource
{
    public SensitiveByteArray GetStorageKey(StorageDrive drive)
    {
        if (!odinContext.PermissionsContext.TryGetDriveStorageKey(drive.Id, out var storageKey))
        {
            throw new OdinSecurityException(
                $"Caller cannot source the storage key for drive {drive.TargetDriveInfo}",
                OdinClientErrorCode.CannotSourceDriveStorageKeyForGrant);
        }

        return storageKey;
    }
}

/// <summary>
/// Caller-scoped with a keyless fallback: the storage key comes from the caller's own permission
/// context when it has one, and is null otherwise.  Unlike <see cref="PermissionContextStorageKeySource"/>
/// it does not throw, because connection-request grants always include the system circles, whose
/// drives (e.g. the profile drive) an app normally cannot read.
/// </summary>
public sealed class PermissionContextOrNoStorageKeySource(IOdinContext odinContext) : IStorageKeySource
{
    public SensitiveByteArray? GetStorageKey(StorageDrive drive)
    {
        return odinContext.PermissionsContext?.TryGetDriveStorageKey(drive.Id, out var storageKey) == true
            ? storageKey
            : null;
    }
}

/// <summary>
/// Deliberately keyless: mints grants without storage keys. Only for grants that carry no
/// read access (write-only, anonymous-drive permission groups) or whose read keys are
/// filled in by a later owner-present re-mint (e.g. app circle grants at peer-CAT conversion).
/// </summary>
public sealed class NoStorageKeySource : IStorageKeySource
{
    public static readonly NoStorageKeySource Instance = new();

    private NoStorageKeySource()
    {
    }

    public SensitiveByteArray? GetStorageKey(StorageDrive drive)
    {
        return null;
    }
}
