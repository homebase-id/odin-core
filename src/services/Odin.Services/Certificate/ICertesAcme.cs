using System.Threading.Tasks;

namespace Odin.Services.Certificate;

#nullable enable

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
    Task<AcmeAccount> CreateAccount(string contactEmail);
    Task<KeysAndCertificates> CreateCertificate(AcmeAccount acmeAccount, string[] domains);
}

//

public interface IAcmeHttp01TokenCache
{
    bool TryGet(string token, out string keyAuth);
    public void Set(string token, string keyAuth);
}

//


