namespace Odin.Core.Services.Certificate;

public interface ICertificateServiceFactory
{
    CertificateService Create(string sslRootPath);
}