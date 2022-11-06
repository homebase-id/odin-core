using Youverse.Core;

namespace Youverse.Provisioning.Services.Registry
{
    /// <summary>
    /// Manages the set of Identities registered with this DI host.
    /// </summary>
    public interface IIdentityRegistry
    {
        // IdentityCertificate ResolveCertificate(string domainName);
        //
        // Identity GetIdentity(string domain);
        
        /// <summary>
        /// Checks if a domain is used/registered.
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<bool> IsDomainRegistered(string domain);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="reg"></param>
        Task Add(IdentityRegistration reg);

        /// <summary>
        /// Gets a list of <see cref="IdentityRegistration"/> based on the paging options sorted by domain name ascending
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions);

        /// <summary>
        /// Gets an <see cref="IdentityRegistration"/> by domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        Task<IdentityRegistration> Get(string domainName);
    }
}