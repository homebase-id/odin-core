using Microsoft.Extensions.Logging;

namespace Odin.Core.Services.Certificate;

public class CertificateServiceFactory : ICertificateServiceFactory
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ICertesAcme _certesAcme;
    private readonly AcmeAccountConfig _accountConfig;
    
    //

    public CertificateServiceFactory(ILogger<CertificateService> logger, ICertesAcme certesAcme, AcmeAccountConfig accountConfig)
    {
        _logger = logger;
        _certesAcme = certesAcme;
        _accountConfig = accountConfig;
    }
    
    //

    public CertificateService Create(string sslRootPath)
    {
        return new CertificateService(_logger, _certesAcme, _accountConfig, sslRootPath);
    }
}
