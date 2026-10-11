using System;
using System.IO;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

/// <summary>
/// Client-computed payload hashes (#1895): validation rules, verification, wire shape, and the golden vector for the
/// encrypted content hash that odin-js, chat-kmp and the sync client test against.
/// </summary>
[TestFixture]
public class PayloadHashTests
{
    private static readonly byte[] Payload = "the stored bytes of a payload"u8.ToArray();

    private static PayloadHash UnencryptedHash(ContentHashAlgorithm algorithm = ContentHashAlgorithm.Blake3)
    {
        var hash = IncrementalContentHash.Compute(algorithm, Payload);
        return new PayloadHash { Algorithm = algorithm, StoredHash = hash, ContentHash = (byte[])hash.Clone() };
    }

    private static PayloadHash EncryptedHash() => new()
    {
        Algorithm = ContentHashAlgorithm.Sha256,
        StoredHash = IncrementalContentHash.Compute(ContentHashAlgorithm.Sha256, Payload),
        ContentHash = new byte[PayloadHash.EncryptedContentHashLength]
    };

    private static OdinClientErrorCode CodeOf(TestDelegate action) => Assert.Throws<OdinClientException>(action)!.ErrorCode;

    //
    // Shape
    //

    [Test]
    public void AWellFormedHashPasses()
    {
        Assert.DoesNotThrow(() => PayloadHash.AssertValid("pk", UnencryptedHash(), isEncrypted: false));
        Assert.DoesNotThrow(() => PayloadHash.AssertValid("pk", EncryptedHash(), isEncrypted: true));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void AnUnknownAlgorithmIsInvalid(int algorithm)
    {
        var hash = UnencryptedHash();
        hash.Algorithm = (ContentHashAlgorithm)algorithm;

        Assert.That(CodeOf(() => hash.AssertIsWellFormed("pk")), Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    [TestCase(null)]
    [TestCase(31)]
    [TestCase(33)]
    public void AStoredHashThatIsNot32BytesIsInvalid(int? length)
    {
        var hash = UnencryptedHash();
        hash.StoredHash = length == null ? null : new byte[length.Value];

        Assert.That(CodeOf(() => hash.AssertIsWellFormed("pk")), Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    [Test]
    public void AStoredHashWithoutAContentHashIsInvalid()
    {
        var hash = UnencryptedHash();
        hash.ContentHash = null;

        Assert.That(CodeOf(() => hash.AssertIsWellFormed("pk")), Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    //
    // Encryption-dependent rules
    //

    [Test]
    public void UnencryptedContentHashMustEqualStoredHash()
    {
        var hash = UnencryptedHash();
        hash.ContentHash[0] ^= 1;

        Assert.That(CodeOf(() => PayloadHash.AssertValid("pk", hash, isEncrypted: false)),
            Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    [TestCase(32)]
    [TestCase(64)]
    public void EncryptedContentHashMustBe48Bytes(int length)
    {
        var hash = EncryptedHash();
        hash.ContentHash = new byte[length];

        Assert.That(CodeOf(() => PayloadHash.AssertValid("pk", hash, isEncrypted: true)),
            Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    [Test]
    public void APlaintextHashOnAnEncryptedPayloadIsRejected()
    {
        // A 32-byte content hash on an encrypted file is a plaintext hash the server should never see
        Assert.That(CodeOf(() => PayloadHash.AssertValid("pk", UnencryptedHash(), isEncrypted: true)),
            Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    [Test]
    public void NoHashIsFineUnlessTheDriveRequiresOne()
    {
        Assert.DoesNotThrow(() => PayloadHash.AssertValid("pk", null, isEncrypted: true));
        Assert.DoesNotThrow(() => PayloadHash.AssertRequired("pk", null, driveRequiresHashes: false));
        Assert.DoesNotThrow(() => PayloadHash.AssertRequired("pk", UnencryptedHash(), driveRequiresHashes: true));
        Assert.That(CodeOf(() => PayloadHash.AssertRequired("pk", null, driveRequiresHashes: true)),
            Is.EqualTo(OdinClientErrorCode.PayloadHashRequired));
    }

    //
    // Binding rule: a hashed overwrite of an encrypted payload must rotate the payload IV
    //

    private static readonly byte[] OldIv = Convert.FromHexString("3c1f8e5a9b2d47e0a6c4d8f1027b9e35");
    private static readonly PayloadDescriptor[] Existing = [new() { Key = "pk", Iv = OldIv }];

    [Test]
    public void AHashedEncryptedOverwriteThatReusesThePayloadIvIsRejected()
    {
        Assert.That(CodeOf(() => PayloadHash.AssertIvRotated("PK", EncryptedHash(), (byte[])OldIv.Clone(), true, Existing)),
            Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash), "payload keys compare case-insensitively");
    }

    [Test]
    public void TheIvRuleOnlyAppliesToHashedEncryptedOverwrites()
    {
        var newIv = PayloadHash.ContentHashIv(OldIv);

        Assert.DoesNotThrow(() => PayloadHash.AssertIvRotated("pk", EncryptedHash(), newIv, true, Existing), "new IV");
        Assert.DoesNotThrow(() => PayloadHash.AssertIvRotated("pk", null, OldIv, true, Existing), "no hash");
        Assert.DoesNotThrow(() => PayloadHash.AssertIvRotated("pk", UnencryptedHash(), OldIv, false, Existing), "not encrypted");
        Assert.DoesNotThrow(() => PayloadHash.AssertIvRotated("other_key", EncryptedHash(), OldIv, true, Existing), "new key");
        Assert.DoesNotThrow(() => PayloadHash.AssertIvRotated("pk", EncryptedHash(), OldIv, true, null), "new file");
    }

    //
    // Verification while streaming
    //

    [TestCase(ContentHashAlgorithm.Sha256)]
    [TestCase(ContentHashAlgorithm.Blake3)]
    public void MatchingBytesVerify(ContentHashAlgorithm algorithm)
    {
        var hash = UnencryptedHash(algorithm);
        using var hashing = ReadAllThrough(hash, Payload);

        Assert.DoesNotThrow(() => hash.AssertStoredHashMatches("pk", hashing));
    }

    [Test]
    public void DifferentBytesAreAMismatch()
    {
        var hash = UnencryptedHash();
        var tampered = (byte[])Payload.Clone();
        tampered[^1] ^= 1;
        using var hashing = ReadAllThrough(hash, tampered);

        Assert.That(CodeOf(() => hash.AssertStoredHashMatches("pk", hashing)), Is.EqualTo(OdinClientErrorCode.PayloadHashMismatch));
    }

    [Test]
    public void VerifyingRejectsAMalformedHashBeforeReadingAnything()
    {
        var hash = UnencryptedHash();
        hash.StoredHash = new byte[4];

        Assert.That(CodeOf(() => hash.Verifying("pk", new MemoryStream(Payload))), Is.EqualTo(OdinClientErrorCode.InvalidPayloadHash));
    }

    private static HashingReadStream ReadAllThrough(PayloadHash hash, byte[] bytes)
    {
        var hashing = hash.Verifying("pk", new MemoryStream(bytes));
        hashing.CopyTo(Stream.Null);
        return hashing;
    }

    //
    // Wire shape
    //

    [Test]
    public void ADescriptorWithoutAHashSerializesWithoutAHashProperty()
    {
        // Existing headers and responses must not change shape: no "hash": null on every payload
        var json = OdinSystemSerializer.Serialize(new PayloadDescriptor { Key = "pk_000001", ContentType = "text/plain" });

        Assert.That(json, Does.Not.Contain("\"hash\""), json);
    }

    [Test]
    public void AHashRoundTripsWithCamelCaseAlgorithmAndBase64Bytes()
    {
        var hash = UnencryptedHash();
        var json = OdinSystemSerializer.Serialize(new PayloadDescriptor { Key = "pk_000001", Hash = hash });

        Assert.That(json, Does.Contain("\"algorithm\":\"blake3\""), json);
        Assert.That(json, Does.Contain($"\"storedHash\":\"{Convert.ToBase64String(hash.StoredHash)}\""), json);

        var restored = OdinSystemSerializer.Deserialize<PayloadDescriptor>(json)!;
        Assert.That(restored.Hash.Algorithm, Is.EqualTo(ContentHashAlgorithm.Blake3));
        Assert.That(restored.Hash.StoredHash, Is.EqualTo(hash.StoredHash));
        Assert.That(restored.Hash.ContentHash, Is.EqualTo(hash.ContentHash));
    }

    [TestCase("\"sha256\"", ContentHashAlgorithm.Sha256)]
    [TestCase("\"blake3\"", ContentHashAlgorithm.Blake3)]
    [TestCase("1", ContentHashAlgorithm.Sha256)]
    [TestCase("2", ContentHashAlgorithm.Blake3)]
    public void TheAlgorithmIsReadByNameOrNumber(string wire, ContentHashAlgorithm expected)
    {
        var restored = OdinSystemSerializer.Deserialize<PayloadHash>($"{{\"algorithm\":{wire}}}")!;

        Assert.That(restored.Algorithm, Is.EqualTo(expected));
    }

    //
    // Golden vector for the encrypted content hash. The expected values were computed independently with Python
    // (hashlib + the cryptography package); the BLAKE3 value is the official test vector for this input.
    //

    private const string VectorKeyHex = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string VectorPayloadIvHex = "3c1f8e5a9b2d47e0a6c4d8f1027b9e35";
    private const string VectorContentHashIvHex = "96b524f03187ed4a0c6e725ba8d1349f";

    [Test]
    public void TheContentHashIvIsThePayloadIvXorAA()
    {
        Assert.That(Convert.ToHexStringLower(PayloadHash.ContentHashIv(Convert.FromHexString(VectorPayloadIvHex))),
            Is.EqualTo(VectorContentHashIvHex));
    }

    // Plaintext: the 1025 bytes 0, 1, ..., 250, 0, 1, ... (as in the BLAKE3 test vectors), AES-256-CBC with PKCS7
    [TestCase(ContentHashAlgorithm.Sha256,
        "bc0b6b10b89b9487a12fda2a8cc13194e7091c217aabf8b92846274026f4bcd0",
        "707e52fbded98a6a5f2e2d29efb31bd4a76cc302e70680437b969ddbf2117a5f76efb8fe04637b3eefb16adced53ad6a")]
    [TestCase(ContentHashAlgorithm.Blake3,
        "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444",
        "fe9fbd96981ea69f6b15430fab88d92acbb1e1d5c28b6d54f86c5d195f678955259f6ef4ccb89d8520416e0e20fc1c3c")]
    public void EncryptedContentHashGoldenVector(ContentHashAlgorithm algorithm, string contentHashHex, string encryptedHex)
    {
        var plaintext = new byte[1025];
        for (var i = 0; i < plaintext.Length; i++)
        {
            plaintext[i] = (byte)(i % 251);
        }

        var contentHash = IncrementalContentHash.Compute(algorithm, plaintext);
        Assert.That(Convert.ToHexStringLower(contentHash), Is.EqualTo(contentHashHex));

        var iv = PayloadHash.ContentHashIv(Convert.FromHexString(VectorPayloadIvHex));
        var encrypted = AesCbc.Encrypt(contentHash, Convert.FromHexString(VectorKeyHex), iv);

        Assert.That(encrypted.Length, Is.EqualTo(PayloadHash.EncryptedContentHashLength));
        Assert.That(Convert.ToHexStringLower(encrypted), Is.EqualTo(encryptedHex));
    }

    //
    // Drive flag
    //

    [Test]
    public void ADriveStoredBeforeTheFlagExistedDoesNotRequireHashes()
    {
        const string legacyJson = """
            {"Metadata":"","OwnerOnly":false,"IsReadonly":false,
             "AllowAnonymousReads":false,"AllowSubscriptions":false,"AllowCdn":false,"IsArchived":false}
            """;

        Assert.That(OdinSystemSerializer.Deserialize<StorageDriveDetails>(legacyJson)!.RequirePayloadHashes, Is.False);
    }

    [Test]
    public void RequirePayloadHashesRoundTripsThroughSerialization()
    {
        var restored = OdinSystemSerializer.Deserialize<StorageDriveDetails>(
            OdinSystemSerializer.Serialize(new StorageDriveDetails { RequirePayloadHashes = true }))!;

        Assert.That(restored.RequirePayloadHashes, Is.True);
        Assert.That(new StorageDrive(null, new StorageDriveData { RequirePayloadHashes = true }).RequirePayloadHashes, Is.True);
    }
}
