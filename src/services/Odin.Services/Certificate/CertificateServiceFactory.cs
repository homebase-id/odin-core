using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Certificate;

public class CertificateServiceFactory : ICertificateServiceFactory
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ICertificateStore _certificateStore;
    private readonly ICertesAcme _certesAcme;
    private readonly IDnsLookupService _dnsLookupService;
    private readonly AcmeAccountConfig _accountConfig;
    private readonly SystemDatabase _systemDatabase;

    //

    public CertificateServiceFactory(
        ILogger<CertificateService> logger,
        ICertificateStore certificateStore,
        ICertesAcme certesAcme,
        IDnsLookupService dnsLookupService,
        AcmeAccountConfig accountConfig,
        SystemDatabase systemDatabase)
    {
        _logger = logger;
        _certificateStore = certificateStore;
        _certesAcme = certesAcme;
        _dnsLookupService = dnsLookupService;
        _accountConfig = accountConfig;
        _systemDatabase = systemDatabase;
    }
    
    //

    public CertificateService Create()
    {
        return new CertificateService(
            _logger,
            _certificateStore,
            _certesAcme,
            _dnsLookupService,
            _accountConfig,
            _systemDatabase);
    }
}
