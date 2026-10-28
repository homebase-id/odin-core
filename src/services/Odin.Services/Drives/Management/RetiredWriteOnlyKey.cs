#nullable enable
using System.Collections.Generic;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Services.Drives.Management;

/// <summary>
/// A write-only key pair a drive replaced (docs/drive-key-retirement-plan.md).  New seals made with it are
/// accepted until <see cref="RetiredAt"/> plus <c>Host:RetiredDriveKeyAcceptSeconds</c>; its private half is kept
/// for good, so the files already sealed to it still open.
/// </summary>
/// <param name="KeyPair">The public half, as <see cref="StorageDrive.WriteOnlyKeyPair"/> held it.</param>
/// <param name="ZkStorageKeyEncryptedWriteOnlyPrivateKey">The private half, locked by the browser.</param>
public sealed record RetiredWriteOnlyKey(
    EccFullKeyData KeyPair,
    string ZkStorageKeyEncryptedWriteOnlyPrivateKey,
    UnixTimeUtc RetiredAt);

/// <summary>Which of a drive's write-only keys a sealed key header was sealed to.</summary>
public enum SealedTo
{
    CurrentKey,

    /// <summary>A retired key still taking new seals: the sender's copy of the public key is out of date.</summary>
    RetiredKey
}

/// <summary>A drive's retired write-only keys, newest first, as stored.</summary>
internal sealed class RetiredWriteOnlyKeys
{
    public List<RetiredWriteOnlyKey> Keys { get; init; } = [];
}
