#nullable enable
using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Fixed values for Selective Share's three key blobs (docs/selective-share-plan.md, "Blob formats"), so every client
/// locks and opens them the same way: a share row's <c>lockedFileKey</c>, a circle's <c>appKeyLockedShareKey</c> and a
/// member's <c>sealedShareKey</c>.  The server never opens any of them; this is the reference the clients (chat-kmp,
/// odin-js, filesync) test against, as <see cref="ClientLockedKeyHeaderVectorTests"/> is for the file key headers.
/// Change them in every client or in none.  The sealed value is made with a throwaway key, so it was made once and is
/// checked by opening it.
/// </summary>
[TestFixture]
public class SelectiveShareKeyVectorTests
{
    private const string CircleKey = "MDEyMzQ1Njc4OTo7PD0+Pw==";       // 0x30..0x3f
    private const string FileKey = "ICEiIyQlJicoKSorLC0uLw==";         // 0x20..0x2f, the AesKey of the file key header vector
    private const string AppZkK = "QEFCQ0RFRkdISUpLTE1OTw==";          // 0x40..0x4f
    private const string FileKeyNonce = "UFFSU1RVVldYWVpb";            // 0x50..0x5b
    private const string AppKeyNonce = "YGFiY2RlZmdoaWpr";             // 0x60..0x6b
    private static readonly Guid DriveId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid FileId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");
    private static readonly Guid CircleId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
    private const string Member = "Sam.DotYou.Cloud";                 // lowercased into the AAD

    private const string LockedFileKey = "AlBRUlNUVVZXWFlaWyNALFXaUVoKLLDbDk0g5bIydyRsCSpKQjTVXgru8mq5";
    private const string AppKeyLockedShareKey = "AmBhYmNkZWZnaGlqa8AlHXzCApsgaEfKX1KFpSBA7DLsgycVGxPm/K9OaD6A";
    private const string SealedShareKey =
        "AhRSmXQE7NVFE2N+zrbujRavkL1z/E4GHalzavkps48544kxb1C5vlxaQZbfnbT2N+RRSjZob4zGxLf4jftVPwAgZq3c692FaLeGasq2pZGKZ1iIyxpJ7Fbilv77slwQlOIeKC7Rag3IKHq3cJUyfbDKapD3G1IWdxUh0JkipMakO1Wy1Um0cvyaU9IBa5FBFpM=";

    [Test]
    public void ALockedFileKeyIsTheFileKeyUnderTheCircleKeyBoundToItsRow()
    {
        var locked = ShareKeys.Lock(CircleKey.FromBase64(), FileKey.FromBase64(), ShareKeys.FileKeyAad(DriveId, FileId, CircleId),
            FileKeyNonce.FromBase64());
        Assert.That((locked.Length, locked.ToBase64()), Is.EqualTo((45, LockedFileKey)));
        Assert.That(ShareKeys.Open(CircleKey.FromBase64(), locked, ShareKeys.FileKeyAad(DriveId, FileId, CircleId)).ToBase64(),
            Is.EqualTo(FileKey));
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            ShareKeys.Open(CircleKey.FromBase64(), locked, ShareKeys.FileKeyAad(DriveId, Guid.NewGuid(), CircleId)), "on another file's row");
    }

    [Test]
    public void AnAppKeyLockedShareKeyIsTheCircleKeyUnderTheAppsKey()
    {
        var locked = ShareKeys.Lock(AppZkK.FromBase64(), CircleKey.FromBase64(), CircleId.ToByteArray(), AppKeyNonce.FromBase64());
        Assert.That((locked.Length, locked.ToBase64()), Is.EqualTo((45, AppKeyLockedShareKey)));
        Assert.That(ShareKeys.Open(AppZkK.FromBase64(), locked, CircleId.ToByteArray()).ToBase64(), Is.EqualTo(CircleKey));
    }

    [Test]
    public void ASealedShareKeyOpensWithTheMembersDriveKeyForThatCircleAndMember()
    {
        var aad = ShareKeys.SealedAad(CircleId, Member);
        var privateKey = ClientLockedKeyHeaderVectorTests.WriteOnlyPrivateKeyPkcs8.FromBase64();
        Assert.That(BinaryPrimitives.ReadUInt32BigEndian(SealedShareKey.FromBase64().AsSpan(1, 4)),
            Is.EqualTo(ZkSeal.KeyId(ClientLockedKeyHeaderVectorTests.WriteOnlyPublicKeyJwk)), "sealed to that key");
        Assert.That(ShareKeys.Unseal(SealedShareKey.FromBase64(), privateKey, aad).ToBase64(), Is.EqualTo(CircleKey));
        Assert.Throws<AuthenticationTagMismatchException>(() =>
            ShareKeys.Unseal(SealedShareKey.FromBase64(), privateKey, ShareKeys.SealedAad(CircleId, "pippin.dotyou.cloud")), "another member's");

        // And what a client makes opens the same way
        var made = ShareKeys.Seal(CircleKey.FromBase64(), ClientLockedKeyHeaderVectorTests.WriteOnlyPublicKeyJwk, aad);
        Assert.That((made.Length, ShareKeys.Unseal(made, privateKey, aad).ToBase64()), Is.EqualTo((146, CircleKey)));
    }

    [Test, Explicit("prints a new sealed value, for when the format changes")]
    public void PrintASealedShareKey() =>
        TestContext.Out.WriteLine(ShareKeys.Seal(CircleKey.FromBase64(), ClientLockedKeyHeaderVectorTests.WriteOnlyPublicKeyJwk,
            ShareKeys.SealedAad(CircleId, Member)).ToBase64());
}

/// <summary>
/// The reference implementation of the three blobs, format 2: AES-128-GCM with a 12-byte nonce and a 16-byte tag.
/// <list type="bullet">
/// <item><c>lockedFileKey</c> = <c>0x02 ‖ nonce ‖ ciphertext(16) ‖ tag</c> under the circle key, AAD
/// <c>driveId ‖ fileId ‖ circleId</c>: 45 bytes.</item>
/// <item><c>appKeyLockedShareKey</c> = the same under the app's AppZkK, AAD <c>circleId</c>: 45 bytes.</item>
/// <item><c>sealedShareKey</c> = <see cref="ZkSeal"/>'s layout with version 2 and an AAD:
/// <c>0x02 ‖ key id (4, big-endian) ‖ throwaway public key (97) ‖ nonce ‖ ciphertext(16) ‖ tag</c>, the AES key
/// hkdf-sha256(ECDH, no salt, "homebase-zk-seal"), AAD <c>circleId ‖ UTF-8(lowercased member odinId)</c>: 146 bytes.</item>
/// </list>
/// Every id is a Guid's .NET <see cref="Guid.ToByteArray()"/> (the first three fields little-endian), as the zero-knowledge
/// drive key check already uses; a drive's id is its alias.
/// </summary>
internal static class ShareKeys
{
    public const byte Format = 2;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public static byte[] FileKeyAad(Guid driveId, Guid fileId, Guid circleId) =>
        ByteArrayUtil.Combine(driveId.ToByteArray(), fileId.ToByteArray(), circleId.ToByteArray());

    public static byte[] SealedAad(Guid circleId, string memberOdinId) =>
        ByteArrayUtil.Combine(circleId.ToByteArray(), Encoding.UTF8.GetBytes(memberOdinId.ToLowerInvariant()));

    public static byte[] Lock(byte[] key, byte[] plaintext, byte[] aad, byte[] nonce)
    {
        using var gcm = new AesGcm(key, TagLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        gcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        return ByteArrayUtil.Combine([Format], nonce, ciphertext, tag);
    }

    public static byte[] Open(byte[] key, byte[] locked, byte[] aad)
    {
        Assert.That(locked[0], Is.EqualTo(Format), "format");
        using var gcm = new AesGcm(key, TagLength);
        var nonce = locked[1..(1 + NonceLength)];
        var ciphertext = locked[(1 + NonceLength)..^TagLength];
        var plaintext = new byte[ciphertext.Length];
        gcm.Decrypt(nonce, ciphertext, locked[^TagLength..], plaintext, aad);
        return plaintext;
    }

    public static byte[] Seal(byte[] plaintext, string publicKeyJwk, byte[] aad)
    {
        var recipientPoint = ZkSeal.RawPublicKey(publicKeyJwk);
        using var recipient = ECDiffieHellman.Create(PublicPoint(recipientPoint));
        using var throwaway = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var q = throwaway.ExportParameters(false).Q;
        var key = ZkSeal.DeriveKey(throwaway, recipient.PublicKey);

        var keyId = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(keyId, ZkSeal.KeyId(publicKeyJwk));
        var locked = Lock(key, plaintext, aad, RandomNumberGenerator.GetBytes(NonceLength));
        return ByteArrayUtil.Combine([Format], keyId, [0x04], q.X!, q.Y!, locked[1..]);
    }

    public static byte[] Unseal(byte[] sealedValue, byte[] privateKeyPkcs8, byte[] aad)
    {
        Assert.That((sealedValue[0], sealedValue[ZkSeal.PublicKeyOffset]), Is.EqualTo((Format, (byte)0x04)), "format");
        using var me = ECDiffieHellman.Create();
        me.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);
        var pointEnd = ZkSeal.PublicKeyOffset + ZkSeal.PublicKeyLength;
        using var throwaway = ECDiffieHellman.Create(PublicPoint(sealedValue[ZkSeal.PublicKeyOffset..pointEnd]));
        var key = ZkSeal.DeriveKey(me, throwaway.PublicKey);
        return Open(key, ByteArrayUtil.Combine([Format], sealedValue[pointEnd..]), aad);
    }

    private static ECParameters PublicPoint(byte[] uncompressed) => new()
    {
        Curve = ECCurve.NamedCurves.nistP384,
        Q = new ECPoint { X = uncompressed[1..(1 + ZkSeal.CoordinateLength)], Y = uncompressed[(1 + ZkSeal.CoordinateLength)..] }
    };
}
