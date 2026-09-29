using System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.DatabaseImport;
using Odin.Services.Certificate;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

/// <summary>
/// The key material a host keeps under its own storage keys, which differ from cluster to cluster by design. The
/// export carries the TLS certificate's key in the clear and the import encrypts it under the target's key, so the
/// certificate works on the target from the start (<see cref="CertificateStore"/>). DKIM keys are left out of the
/// export (IdentityJsonTransfer): email does not move with an identity (agents/identity-move/README.md).
/// </summary>
internal static class IdentityKeyMaterial
{
    public static RowRewriter ForExport(byte[] sourceCertificateKey) => (_, _, record) =>
        record is CertificatesRecord certificate ? InTheClear(certificate, sourceCertificateKey) : record;

    public static RowRewriter ForImport(byte[] targetCertificateKey) => (_, _, record) =>
        record is CertificatesRecord certificate ? Encrypted(certificate, targetCertificateKey) : record;

    private static CertificatesRecord InTheClear(CertificatesRecord certificate, byte[] sourceCertificateKey)
    {
        try
        {
            return CertificateStore.WithKeyInTheClear(certificate, sourceCertificateKey);
        }
        catch (Exception e)
        {
            throw new IdentityExportRefusedException(
                $"The certificate key for {certificate.domain.DomainName} cannot be read with this host's CertificateRenewal:StorageKey: {e.Message}", e);
        }
    }

    private static CertificatesRecord Encrypted(CertificatesRecord certificate, byte[] targetCertificateKey)
    {
        try
        {
            return CertificateStore.WithKeyEncrypted(certificate, targetCertificateKey);
        }
        catch (Exception e)
        {
            throw new IdentityImportRefusedException(
                $"The certificate key for {certificate.domain.DomainName} in the file does not fit its certificate: {e.Message}", e);
        }
    }
}
