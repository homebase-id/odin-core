using System.Security.Cryptography.X509Certificates;

namespace DotYou.TenantHost.Certificate
{
    public interface ICertificateResolver
    {
        /// <summary>
        /// Returns the <see cref="X509Certificate2"/> for a given hostname (i.e. frodo.youfoundation.id).  The hostname be of DNS type and not include any protocol or port information. 
        /// </summary>
        X509Certificate2 Resolve(string hostname);
    }
}
