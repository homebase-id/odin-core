using System;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.X509;
using Odin.Hosting.Cli.Commands;
using Odin.Services.Certificate;

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
        var stored = CertificateRow(CertificateStore.EncryptPrivateKey(keyPem, certificatePem, SourceKey), certificatePem);

        var exported = (CertificatesRecord)Export(stored)!;
        Assert.That(exported.privateKey, Is.EqualTo(keyPem), "the file carries the key in the clear");

        var imported = (CertificatesRecord)IdentityKeyMaterial.ForImport(TargetKey)(IdentityExportFile.DbSystem, "Certificates", exported)!;
        Assert.That(CertificateStore.DecryptPrivateKey(imported.privateKey, certificatePem, TargetKey), Is.EqualTo(keyPem),
            "the target reads it with its own key");
        Assert.That(imported.certificate, Is.EqualTo(certificatePem));
    }

    [Test]
    public void ARowWithoutAKeyPassesThroughUnchanged()
    {
        var failedIssuance = CertificateRow("", "");
        Assert.That(Export(failedIssuance), Is.SameAs(failedIssuance));
        Assert.That(IdentityKeyMaterial.ForImport(TargetKey)(IdentityExportFile.DbSystem, "Certificates", failedIssuance),
            Is.SameAs(failedIssuance));
    }

    [Test]
    public void TheExportLeavesDkimKeysBehind()
    {
        var dkim = new DkimKeysRecord { domain = new OdinId(Domain), selector = "hb1", privateKey = "encrypted" };
        Assert.That(Export(dkim, "DkimKeys"), Is.Null);
    }

    [Test]
    public void TheExportRefusesACertificateKeyItCannotRead()
    {
        var (keyPem, certificatePem) = NewCertificate();
        var storedUnderAnotherKey = CertificateRow(CertificateStore.EncryptPrivateKey(keyPem, certificatePem, TargetKey), certificatePem);

        var e = Assert.Throws<InvalidOperationException>(() => Export(storedUnderAnotherKey));
        Assert.That(e!.Message, Does.Contain("CertificateRenewal:StorageKey"));
    }

    [Test]
    public void TheImportRefusesAKeyThatDoesNotFitItsCertificate()
    {
        var (_, certificatePem) = NewCertificate();
        var (otherKeyPem, _) = NewCertificate();

        var e = Assert.Throws<IdentityImportRefusedException>(() =>
            IdentityKeyMaterial.ForImport(TargetKey)(IdentityExportFile.DbSystem, "Certificates", CertificateRow(otherKeyPem, certificatePem)));
        Assert.That(e!.Message, Does.Contain("does not fit its certificate"));
    }

    //

    private static object? Export(object record, string table = "Certificates") =>
        IdentityKeyMaterial.ForExport(SourceKey, NullLogger.Instance)(IdentityExportFile.DbSystem, table, record);

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
