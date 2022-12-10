using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Registry
{
    public interface IIdentityRegistry
    {
        void Initialize();

        Guid ResolveId(string domain);

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

        /// <summary>
        /// Starts process of creating a certificate set if the domain does not have a valid set
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task EnsureCertificate(string domain);

        /// <summary>
        /// Processes through all <see cref="IdentityRegistration"/>s to ensure they have valid certificates
        /// </summary>
        /// <returns></returns>
        Task EnsureCertificates();

        Task MarkRegistrationComplete(Guid firstRunToken);

        /// <summary>
        /// Returns the registration status for the identity
        /// </summary>
        Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken);
    }
}