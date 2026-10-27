#nullable enable
using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Services.Authorization.Apps;

/// <summary>
/// An app's zero-knowledge keys as the owner console reads them, to give a device the app's key at sign-in, to backfill
/// what is missing, and to know which apps need a copy when a drive is converted (docs/zero-knowledge-drive-api-plan.md,
/// step 3): the app's key locked with the zero-knowledge master key, and each drive the app is granted; on a
/// zero-knowledge drive, the drive's key locked with the zero-knowledge master key and whether the app has a copy locked
/// with its own key yet.  All opaque to the server.
/// </summary>
public sealed class ZkAppKeysResponse
{
    public Guid AppId { get; init; }
    public string AppSlug { get; init; } = "";
    public string? ZkMasterKeyEncryptedAppKey { get; init; }
    public List<ZkAppDrive> Drives { get; init; } = [];
}

public sealed class ZkAppDrive
{
    public Guid DriveId { get; init; }
    public bool IsZeroKnowledge { get; init; }

    /// <summary>On a zero-knowledge drive, its key locked with the zero-knowledge master key; null otherwise.</summary>
    public string? ZkMasterKeyEncryptedStorageKey { get; init; }

    public bool HasAppCopy { get; init; }
}

/// <summary>
/// What the browser locked for an app: its zero-knowledge key, when the app has none yet, and the keys of
/// zero-knowledge drives the app is granted, each locked with that key.
/// </summary>
public sealed class ZkAppKeysRequest
{
    public Guid AppId { get; set; }

    /// <summary>The app's new zero-knowledge key, locked with the zero-knowledge master key; only when it has none.</summary>
    public string? ZkMasterKeyEncryptedAppKey { get; set; }

    /// <summary>By drive id: the drive's key locked with the app's zero-knowledge key.</summary>
    public Dictionary<Guid, string> ZkAppKeyEncryptedStorageKeys { get; set; } = new();

    public void Validate()
    {
        if (AppId == Guid.Empty)
        {
            throw new OdinClientException("An app id is required", OdinClientErrorCode.ArgumentError);
        }

        if (ZkMasterKeyEncryptedAppKey != null)
        {
            ZkBlob.AssertLocked(ZkMasterKeyEncryptedAppKey, nameof(ZkMasterKeyEncryptedAppKey), maxLength: 1024);
        }

        if (ZkAppKeyEncryptedStorageKeys == null)
        {
            throw new OdinClientException($"{nameof(ZkAppKeyEncryptedStorageKeys)} is required (empty for none)",
                OdinClientErrorCode.ArgumentError);
        }

        foreach (var (driveId, locked) in ZkAppKeyEncryptedStorageKeys)
        {
            ZkBlob.AssertLocked(locked, $"{nameof(ZkAppKeyEncryptedStorageKeys)}[{driveId}]", maxLength: 1024);
        }
    }
}
