using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Registry;

namespace Odin.Services.Certificate;

#nullable enable

public interface ICertificateService
{
    /// <summary>
    /// Returns the SSL certificate for the current OdinId
    /// </summary>
    /// <param name="domain"></param>
    /// <returns></returns>
    Task<X509Certificate2?> GetCertificateAsync(string domain);

    /// <summary>
    /// Stores the SSL certificate for the current OdinId from PEM strings
    /// </summary>
    /// <returns>X509Certificate2</returns>
    Task<X509Certificate2> PutCertificateAsync(string domain, KeysAndCertificates pems);

    /// <summary>
    /// Create certificate for domain. Never waits: returns null if a certificate cannot be
    /// issued right now (an order is already in flight, or the domain is in failure backoff).
    /// </summary>
    Task<X509Certificate2?> CreateCertificateAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create certificate for domain with sans (Subject Alternative Names). Never waits: returns
    /// null if a certificate cannot be issued right now (an order is already in flight, or the
    /// domain is in failure backoff). Safe to call from the TLS handshake path.
    /// </summary>
    Task<X509Certificate2?> CreateCertificateAsync(string domain, string[] sans, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renew certificate for domain if about to expire
    /// </summary>
    Task<bool> RenewIfAboutToExpireAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renew certificate for domain with sans (Subject Alternative Names) if about to expire
    /// </summary>
    Task<bool> RenewIfAboutToExpireAsync(string domain, string[] sans, CancellationToken cancellationToken = default);
}

public class AcmeAccountConfig
{
    public string AcmeContactEmail { get; set; } = "";
}