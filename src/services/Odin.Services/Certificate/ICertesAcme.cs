using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.Certificate;

#nullable enable

// SEB:NOTE Certes is no longer maintained and does not support CancellationToken.
// https://github.com/fszlin/certes

public class AcmeAccount
{
    public string AccounKeyPem { get; set; } = "";
}

public class KeysAndCertificates
{
    public string PrivateKeyPem { get; set; } = "";
    public string CertificatesPem { get; set; } = "";
}

//

public interface ICertesAcme
{
    bool IsProduction { get; }
    Task<AcmeAccount> CreateAccountAsync(string contactEmail, CancellationToken cancellationToken = default);
    Task<KeysAndCertificates> CreateCertificateAsync(AcmeAccount acmeAccount, string[] domains, CancellationToken cancellationToken = default);
}

//



