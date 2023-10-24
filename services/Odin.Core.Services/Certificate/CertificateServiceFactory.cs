using Microsoft.Extensions.Logging;
using Odin.Core.Services.Registry.Registration;

namespace Odin.Core.Services.Certificate;

public class CertificateServiceFactory : ICertificateServiceFactory
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ICertesAcme _certesAcme;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly AcmeAccountConfig _accountConfig;
    
    //

    public CertificateServiceFactory(
        ILogger<CertificateService> logger,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig)
    {
        _logger = logger;
        _certesAcme = certesAcme;
        _dnsLookupService = dnsLookupService;
        _accountConfig = accountConfig;
    }
    
    //

    public CertificateService Create(string sslRootPath)
    {
        return new CertificateService(_logger, _certesAcme, _dnsLookupService, _accountConfig, sslRootPath);
    }
}
