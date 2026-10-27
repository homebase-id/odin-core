#nullable enable
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Peer.Encryption;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Fixed values for the two client-locked key headers (docs/zero-knowledge-drive-api-plan.md, step 2), so
/// every client locks and opens them the same way.  The same values are in odin-js
/// (<c>packages/libs/js-lib/src/core/DriveData/SecurityHelpers.test.ts</c>); change both or neither.  A
/// sealed value is made with a throwaway key, so it was made once and is checked by opening it.
/// </summary>
[TestFixture]
public class ClientLockedKeyHeaderVectorTests
{
    private const string DriveKey = "AAECAwQFBgcICQoLDA0ODw==";
    private const string Iv = "EBESExQVFhcYGRobHB0eHw==";
    private const string AesKey = "ICEiIyQlJicoKSorLC0uLw==";
    private const string ZkDriveEncryptedAesKey = "xqE7N4ePW4JvT4FiocjYeQeFPxlO+hclYcV072RPhmMJbAd9FxFTFUuXv4ql/85L";

    private const string WriteOnlyPrivateKeyPkcs8 =
        "MIG2AgEAMBAGByqGSM49AgEGBSuBBAAiBIGeMIGbAgEBBDCZAel29rv0BgBwa7LGp4+tmSi5g4LlRzkW9/ZLQNgeVdrB44imUOGeaKmn1eiPZsShZANiAATKPHfd0ojszgjchR06XVN2p/iaosbJedgQGD86jcH39CM7EjWiXa6wVTW3cWGdDXwWQYx4NHDg8lIWULARJhvn02W1/eoTtDkhdpQrY6KmVbxpY/ehNZlDlhlh3MntF40=";

    private const string WriteOnlyPublicKeyJwk =
        "{\"kty\":\"EC\",\"crv\":\"P-384\",\"x\":\"yjx33dKI7M4I3IUdOl1Tdqf4mqLGyXnYEBg_Oo3B9_QjOxI1ol2usFU1t3FhnQ18\",\"y\":\"FkGMeDRw4PJSFlCwESYb59Nltf3qE7Q5IXaUK2OiplW8aWP3oTWZQ5YZYdzJ7ReN\"}";

    private const string SealedEncryptedAesKey =
        "ARRSmXQEgEhVDqJf8V4wR/vfRnjuBiMDCgpqQ0EvwP8Sbhg/htjK6DPN0tPu9kf4VKIvY0JpI7ekZehCr+RJr6buacGObBkW+4KXwPFn4aNzze/w+3mTcGuj+8oKsvICcbFc5rPogLwn6Oz/OQuWmSn3xtY70ljeN7b6eV3wkBqmS0sKbr1o/iBvA+hgSlg/4KiDGsTq2pxlrCRLPvr2Vxjk";

    private static KeyHeader FileKey => new() { Iv = Iv.FromBase64(), AesKey = AesKey.FromBase64().ToSensitiveByteArray() };

    [Test]
    public void AZkDriveKeyHeaderIsTheFileKeyLockedWithTheDriveKey()
    {
        var locked = ZkBrowser.LockFileKey(FileKey, DriveKey.FromBase64());
        Assert.That(locked.EncryptedAesKey.ToBase64(), Is.EqualTo(ZkDriveEncryptedAesKey));

        var opened = ZkBrowser.OpenFileKey(locked, DriveKey.FromBase64());
        Assert.That((opened.Iv.ToBase64(), opened.AesKey.GetKey().ToBase64()), Is.EqualTo((Iv, AesKey)));
    }

    [Test]
    public void ASealedKeyHeaderOpensWithTheDrivesWriteOnlyPrivateKey()
    {
        Assert.That(ZkSeal.IsSealedTo(SealedEncryptedAesKey.FromBase64(), WriteOnlyPublicKeyJwk), Is.True, "sealed to that key");

        var opened = KeyHeader.FromCombinedBytes(ZkBrowser.Unseal(SealedEncryptedAesKey, WriteOnlyPrivateKeyPkcs8.FromBase64()));
        Assert.That((opened.Iv.ToBase64(), opened.AesKey.GetKey().ToBase64()), Is.EqualTo((Iv, AesKey)));
    }
}
