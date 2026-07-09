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
    /// The pre-existing implicit contract, made explicit: source via the master key when
    /// present, otherwise mint keyless. Use only where a null master key is a legitimate
    /// state (e.g. introduction/auto-connect accept before the deferred upgrade).
    /// </summary>
    public static IStorageKeySource FromMasterKeyOrNone(SensitiveByteArray? masterKey)
    {
        return masterKey == null ? NoStorageKeySource.Instance : new MasterKeyStorageKeySource(masterKey);
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
            throw new OdinSecurityException($"Caller cannot source the storage key for drive {drive.TargetDriveInfo}");
        }

        return storageKey;
    }
}

/// <summary>
/// Deliberately keyless: mints grants without storage keys. Only for grants that carry no
/// read access (write-only, anonymous-drive permission groups) or that are re-minted with
/// real keys later (introduction/auto-connect accept before the master key is online).
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
