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
    /// Ask for a certificate to be issued for domain, out of band. Returns immediately and
    /// yields no certificate: the caller is telling the background issuer that a domain needs
    /// one, not waiting for it. This is the ONLY certificate-creating call that may be made
    /// from a request path.
    /// </summary>
    Task RequestIssuanceAsync(string domain);

    /// <summary>
    /// Place an ACME order for domain and wait for it. Background use only - this blocks for
    /// the duration of the order, and its cancellation token must have application lifetime.
    /// Request paths want <see cref="RequestIssuanceAsync"/>.
    /// </summary>
    Task<X509Certificate2?> CreateCertificateAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Place an ACME order for domain with sans (Subject Alternative Names) and wait for it.
    /// Background use only - see the overload above.
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