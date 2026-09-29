using System;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Services.Certificate;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

/// <summary>
/// The key material a host keeps under its own storage keys, which differ from cluster to cluster by design:
/// <c>CertificateRenewal:StorageKey</c> and <c>Email:DkimStorageKey</c>. The export decrypts the TLS
/// certificate's private key with the source's key and the import encrypts it with the target's, so the
/// certificate works on the target from the start. DKIM keys stay behind: email does not move with an identity.
/// </summary>
internal static class IdentityKeyMaterial
{
    private const string Certificates = "Certificates";
    private const string DkimKeys = "DkimKeys";

    public static RowRewriter ForExport(byte[] sourceCertificateKey, ILogger logger) => (db, table, record) =>
        (db, table, record) switch
        {
            (IdentityExportFile.DbSystem, DkimKeys, DkimKeysRecord dkim) => LeaveBehind(dkim, logger),
            (IdentityExportFile.DbSystem, Certificates, CertificatesRecord certificate) when HasKey(certificate) =>
                certificate with { privateKey = Decrypt(certificate, sourceCertificateKey) },
            _ => record
        };

    public static RowRewriter ForImport(byte[] targetCertificateKey) => (db, table, record) =>
        (db, table, record) switch
        {
            (IdentityExportFile.DbSystem, Certificates, CertificatesRecord certificate) when HasKey(certificate) =>
                certificate with { privateKey = Encrypt(certificate, targetCertificateKey) },
            _ => record
        };

    // A row from a failed first issuance has no key
    private static bool HasKey(CertificatesRecord certificate) => !string.IsNullOrEmpty(certificate.privateKey);

    private static string Decrypt(CertificatesRecord certificate, byte[] sourceCertificateKey)
    {
        var domain = certificate.domain.DomainName;
        try
        {
            var keyPem = CertificateStore.DecryptPrivateKey(certificate.privateKey, certificate.certificate, sourceCertificateKey);
            CertificateStore.AssertKeyFitsCertificate(domain, keyPem, certificate.certificate);
            return keyPem;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"The certificate key for {domain} cannot be read with this host's CertificateRenewal:StorageKey: {e.Message}", e);
        }
    }

    private static string Encrypt(CertificatesRecord certificate, byte[] targetCertificateKey)
    {
        var domain = certificate.domain.DomainName;
        try
        {
            CertificateStore.AssertKeyFitsCertificate(domain, certificate.privateKey, certificate.certificate);
        }
        catch (Exception e)
        {
            throw new IdentityImportRefusedException($"The certificate key for {domain} in the file does not fit its certificate: {e.Message}", e);
        }

        return CertificateStore.EncryptPrivateKey(certificate.privateKey, certificate.certificate, targetCertificateKey);
    }

    private static object? LeaveBehind(DkimKeysRecord dkim, ILogger logger)
    {
        logger.LogWarning(
            "Leaving DKIM key {selector} of {domain} behind: this identity has email, and email (mailbox, messages, "
            + "settings, DKIM keys) does not move yet. See agents/identity-move/README.md",
            dkim.selector, dkim.domain);
        return null;
    }
}
