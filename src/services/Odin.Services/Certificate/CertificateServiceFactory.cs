using Microsoft.Extensions.Logging;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Certificate;

public class CertificateServiceFactory : ICertificateServiceFactory
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ICertificateCache _certificateCache;
    private readonly ICertesAcme _certesAcme;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly AcmeAccountConfig _accountConfig;
    
    //

    public CertificateServiceFactory(
        ILogger<CertificateService> logger,
        ICertificateCache certificateCache,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig)
    {
        _logger = logger;
        _certificateCache = certificateCache;
        _certesAcme = certesAcme;
        _dnsLookupService = dnsLookupService;
        _accountConfig = accountConfig;
    }
    
    //

    public CertificateService Create(string sslRootPath)
    {
        return new CertificateService(
            _logger, _certificateCache, _certesAcme, _dnsLookupService, _accountConfig, sslRootPath);
    }
}
