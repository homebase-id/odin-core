using System;
using System.Linq;
using System.Security.Cryptography;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.X509;
using Odin.Hosting.Cli.Commands;

namespace Odin.Hosting.Tests.Cli;

#nullable enable

// The clusters keep certificate keys under different storage keys, by design; a move re-keys them
public class IdentityKeyMaterialTests
{
    private const string Domain = "frodo.dotyou.cloud";

    private static readonly byte[] SourceKey = Convert.FromHexString("DECAFBADDECAFBADDECAFBADDECAFBADDECAFBADDECAFBADDECAFBADDECAFBAD");
    private static readonly byte[] TargetKey = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");

    [Test]
    public void TheCertificateKeyTravelsFromTheSourcesStorageKeyToTheTargets()
    {
        var (keyPem, certificatePem) = NewCertificate();
        var storedOnSource = Import(CertificateRow(keyPem, certificatePem), SourceKey);
        Assert.That(storedOnSource.privateKey, Is.Not.EqualTo(keyPem), "kept encrypted at rest");

        var exported = Export(storedOnSource, SourceKey);
        Assert.That(exported.privateKey, Is.EqualTo(keyPem), "the file carries the key in the clear");

        var storedOnTarget = Import(exported, TargetKey);
        Assert.That(Export(storedOnTarget, TargetKey).privateKey, Is.EqualTo(keyPem), "the target reads it with its own key");
        Assert.That(storedOnTarget.certificate, Is.EqualTo(certificatePem));
    }

    [Test]
    public void ARowWithoutAKeyPassesThroughUnchanged()
    {
        var failedIssuance = CertificateRow("", "");
        Assert.That(Export(failedIssuance, SourceKey), Is.SameAs(failedIssuance));
        Assert.That(Import(failedIssuance, TargetKey), Is.SameAs(failedIssuance));
    }

    [Test]
    public void TheExportRefusesACertificateKeyItCannotRead()
    {
        var (keyPem, certificatePem) = NewCertificate();
        var storedUnderAnotherKey = Import(CertificateRow(keyPem, certificatePem), TargetKey);

        var e = Assert.Throws<IdentityExportRefusedException>(() => Export(storedUnderAnotherKey, SourceKey));
        Assert.That(e!.Message, Does.Contain("CertificateRenewal:StorageKey"));
    }

    // A wrong key passed the padding check in 2 of the CI runs before the export checked the key against its
    // certificate. The padding sits in the last block, which the IV does not reach, so any IV finds such a key.
    [Test]
    public void TheExportRefusesAWrongKeyWhoseDecryptionHappensToLookValid()
    {
        var (keyPem, certificatePem) = NewCertificate();
        var storedUnderAnotherKey = Import(CertificateRow(keyPem, certificatePem), TargetKey);
        var cipher = Convert.FromHexString(storedUnderAnotherKey.privateKey);

        var wrongKey = Enumerable.Range(0, 100_000)
            .Select(_ => ByteArrayUtil.GetRndByteArray(32))
            .First(key => DecryptsWithoutError(cipher, key));

        var e = Assert.Throws<IdentityExportRefusedException>(() => Export(storedUnderAnotherKey, wrongKey));
        Assert.That(e!.Message, Does.Contain("CertificateRenewal:StorageKey"));
    }

    private static bool DecryptsWithoutError(byte[] cipher, byte[] key)
    {
        try
        {
            AesCbc.Decrypt(cipher, key, new byte[16]);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    [Test]
    public void TheImportRefusesAKeyThatDoesNotFitItsCertificate()
    {
        var (_, certificatePem) = NewCertificate();
        var (otherKeyPem, _) = NewCertificate();

        var e = Assert.Throws<IdentityImportRefusedException>(() => Import(CertificateRow(otherKeyPem, certificatePem), TargetKey));
        Assert.That(e!.Message, Does.Contain("does not fit its certificate"));
    }

    //

    private static CertificatesRecord Export(CertificatesRecord row, byte[] key) =>
        (CertificatesRecord)IdentityKeyMaterial.ForExport(key)(IdentityExportFile.DbSystem, "Certificates", row);

    private static CertificatesRecord Import(CertificatesRecord row, byte[] key) =>
        (CertificatesRecord)IdentityKeyMaterial.ForImport(key)(IdentityExportFile.DbSystem, "Certificates", row);

    private static (string keyPem, string certificatePem) NewCertificate()
    {
        using var x509 = X509Extensions.CreateSelfSignedEcDsaCertificate(Domain);
        return x509.ExtractEcDsaPemData();
    }

    private static CertificatesRecord CertificateRow(string privateKey, string certificatePem) => new()
    {
        domain = new OdinId(Domain),
        privateKey = privateKey,
        certificate = certificatePem,
    };
}
