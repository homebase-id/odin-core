using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Odin.Core.Util;
using Odin.Core.X509;

namespace Odin.PushNotification;

public interface ISignatureCheck
{
    Task<bool> Validate(string domain, byte[] signature, string messageId);
    void AddCertificate(string domain, X509Certificate2 certificate);
    Task<X509Certificate2?> DownloadCertificate(string domain);
}

public class SignatureCheck(
    ILogger<SignatureCheck> logger,
    bool acceptBadCertificates = false) : ISignatureCheck
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _publicCertCache = new();

    //

    public async Task<bool> Validate(string domain, byte[] signature, string messageId)
    {
        if (!AsciiDomainNameValidator.TryValidateDomain(domain))
        {
            return false;
        }

        if (_publicCertCache.TryGetValue(domain, out var certificate))
        {
            if (certificate.VerifySignature(signature, messageId))
            {
                return true;
            }
        }

        // Not in cache or not matching. Download and try again.
        certificate = await DownloadCertificate(domain);
        if (certificate != null)
        {
            AddCertificate(domain, certificate);
            if (certificate.VerifySignature(signature, messageId))
            {
                return true;
            }
        }

        return false;
    }

    //

    public void AddCertificate(string domain, X509Certificate2 certificate)
    {
        _publicCertCache.AddOrUpdate(domain, certificate, (_,_) => certificate);
    }

    //

    public async Task<X509Certificate2?> DownloadCertificate(string domain)
    {
        try
        {
            using var client = new TcpClient(domain, 443);
            await using var sslStream = new SslStream(
                client.GetStream(),
                false,
                ValidateServerCertificate,
                null
            );

            await sslStream.AuthenticateAsClientAsync(domain);
            var serverCertificate = sslStream.RemoteCertificate;

            if (serverCertificate == null)
            {
                logger.LogDebug("{Domain} has no TLS certificate", domain);
                return null;
            }

            // Convert the certificate to X509Certificate2
            var certificate2 = new X509Certificate2(serverCertificate);

            logger.LogDebug("{Domain} public key: {PublicKey}", domain, certificate2.GetPublicKeyString());

            return certificate2;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error downloading/validating certificate for {Domain}", domain);
            return null;
        }
    }

    //

    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (acceptBadCertificates)
        {
            logger.LogWarning("Accepting all certificates");
            return true;
        }

        if (certificate == null)
        {
            logger.LogDebug("No certificate provided");
            return false;
        }

        if (chain == null)
        {
            logger.LogDebug("No chain provided");
            return false;
        }

        if (sslPolicyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        // Attempt to build the certificate chain and check for errors
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.Build(new X509Certificate2(certificate));

        if (chain.ChainStatus.Length != 0)
        {
            foreach (var status in chain.ChainStatus)
            {
                logger.LogDebug("Chain error: {Status} - {Information}", status.Status, status.StatusInformation);
            }
            return false;
        }

        // No chain errors, trust the certificate
        return true;
    }
}
