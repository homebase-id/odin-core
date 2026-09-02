using System;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Core.Cryptography.Data;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives;

/// <summary>
/// The drive's write-only keypair: an ECC-384 key whose private half is escrowed under the drive's
/// own storage key, so that anyone can seal a deposit to the drive but only a holder of the storage
/// key can unseal it (docs/drive-addressing.md).
/// </summary>
/// <remarks>
/// What is worth pinning here is the escrow, not the curve.  The whole security claim is "custody of
/// deposits equals existing read access" -- which holds only if the private half is genuinely
/// unreachable without the storage key, and genuinely reachable with it.
/// </remarks>
public class DriveWriteOnlyKeyTests
{
    private static SensitiveByteArray NewStorageKey() =>
        new(ByteArrayUtil.GetRndByteArray(16));

    [Test]
    public void MintsAKeypairWhosePrivateHalfOpensWithTheStorageKey()
    {
        var storageKey = NewStorageKey();

        var keyPair = DriveWriteOnlyKey.CreateKeyPair(storageKey);

        Assert.That(keyPair, Is.Not.Null);
        Assert.That(keyPair.publicKey, Is.Not.Null.And.Not.Empty);
        Assert.That(() => keyPair.privateDerBase64(storageKey), Throws.Nothing);
    }

    [Test]
    public void ThePrivateHalfDoesNotOpenWithAnotherKey()
    {
        // The claim the whole design rests on: holding the public half buys you deposit, not read.
        var keyPair = DriveWriteOnlyKey.CreateKeyPair(NewStorageKey());

        Assert.That(() => keyPair.privateDerBase64(NewStorageKey()), Throws.Exception);
    }

    [Test]
    public void SurvivesTheColumnRoundTrip()
    {
        var storageKey = NewStorageKey();
        var original = DriveWriteOnlyKey.CreateKeyPair(storageKey);

        var restored = DriveWriteOnlyKey.Deserialize(DriveWriteOnlyKey.Serialize(original));

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored.publicKey, Is.EqualTo(original.publicKey));

        // Serializing must carry the escrowed private half too, not just the public one -- a drive
        // that could not unseal its own deposits would look fine until the first one arrived.
        Assert.That(() => restored.privateDerBase64(storageKey), Throws.Nothing);
    }

    [Test]
    public void NullMeansNoKeypairInBothDirections()
    {
        // NULL in the column is what says "deposits not enabled" for that drive, so neither direction
        // may invent a value.
        Assert.That(DriveWriteOnlyKey.Serialize(null), Is.Null);
        Assert.That(DriveWriteOnlyKey.Deserialize(null), Is.Null);
        Assert.That(DriveWriteOnlyKey.Deserialize([]), Is.Null);
    }

    [Test]
    public void TwoDrivesGetDifferentKeys()
    {
        var a = DriveWriteOnlyKey.CreateKeyPair(NewStorageKey());
        var b = DriveWriteOnlyKey.CreateKeyPair(NewStorageKey());

        Assert.That(a.publicKey, Is.Not.EqualTo(b.publicKey));
    }

    [Test]
    public void TheKeyOutlivesAnyReasonableDriveLifetime()
    {
        // EccFullKeyData requires a lifespan; the drive keypair's is meant to be effectively
        // unbounded, so an expiry anywhere near normal use would be a bug rather than a policy.
        var keyPair = DriveWriteOnlyKey.CreateKeyPair(NewStorageKey());

        Assert.That(keyPair.IsExpired(), Is.False);
        Assert.That(keyPair.expiration.milliseconds,
            Is.GreaterThan(UnixTimeUtc.Now().AddSeconds(60 * 60 * 24 * 365 * 10).milliseconds),
            "should still be valid a decade out");
    }
}
