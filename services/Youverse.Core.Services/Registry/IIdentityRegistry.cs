using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Registry
{
    public interface IIdentityRegistry
    {
        void Initialize();

        Guid? ResolveId(string domain);

        /// <summary>
        /// Checks if a domain is used/registered.
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<bool> IsIdentityRegistered(string domain);

        /// <summary>
        /// Adds an identity to this host
        /// </summary>
        /// <param name="request"></param>
        Task<Guid> AddRegistration(IdentityRegistrationRequest request);


        /// <summary>
        /// Fully deletes a registration and all data; use with caution
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task DeleteRegistration(string domain);
        
        /// <summary>
        /// Gets a list of <see cref="IdentityRegistration"/>s based on the paging options sorted by domain name ascending
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions = null);

        /// <summary>
        /// Gets an <see cref="IdentityRegistration"/> by domain name
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<IdentityRegistration> Get(string domain);

        Task MarkRegistrationComplete(Guid firstRunToken);

        /// <summary>
        /// Returns the registration status for the identity
        /// </summary>
        Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken);
    }
}