namespace Youverse.Core.Services.Certificate;

public interface ICertificateServiceFactory
{
    CertificateService Create(string sslRootPath);
}