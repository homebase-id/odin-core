using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Time;
using Odin.Services.Base;

namespace Odin.Services.Registry
{
    public interface IIdentityRegistry
    {
        public Task LoadRegistrations();

        /// <summary>
        /// Returns ID for *exact* domain, e.g. www.frodo.me 
        /// </summary>
        /// <param name="domain"></param>
        /// <returns>ID found, otherwise null</returns>
        Guid? ResolveId(string domain);

        /// <summary>
        /// Returns IdentityRegistration for *base* domain and prefix if any, e.g. www.frodo.me 
        /// </summary>
        /// <param name="domain">base domain to lookup, optionally with prefix</param>
        /// /// <param name="prefix">prefix if any</param>
        /// <returns>IdentityRegistration if found, otherwise null</returns>
        IdentityRegistration ResolveIdentityRegistration(string domain, out string prefix);

        public TenantContext CreateTenantContext(string domain, bool updateFileSystem = false);
        public TenantContext CreateTenantContext(IdentityRegistration domain, bool updateFileSystem = false);

        /// <summary>
        /// Checks if a domain is used/registered.
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<bool> IsIdentityRegistered(string domain);

        /// <summary>
        /// Test if domain can be addded as a new registration
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<bool> CanAddNewRegistration(string domain);

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
        /// Copies domain registration and payloads to another path
        /// </summary>
        /// <param name="domain"></param>
        /// <param name="targetRootPath"></param>
        /// <returns>Path to copy</returns>
        Task<string> CopyRegistration(string domain, string targetRootPath);

        /// <summary>
        /// Gets a list of <see cref="IdentityRegistration"/>s based on the paging options sorted by domain name ascending
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions = null);
        Task<List<IdentityRegistration>> GetTenants();

        /// <summary>
        /// Gets an <see cref="IdentityRegistration"/> by domain name
        /// </summary>
        /// <param name="domain"></param>
        /// <returns></returns>
        Task<IdentityRegistration> GetAsync(string domain);

        Task MarkRegistrationComplete(Guid firstRunToken);

        /// <summary>
        /// Returns the registration status for the identity
        /// </summary>
        Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken);

        /// <summary>
        /// Toggles disabled on/off
        /// </summary>
        /// /// <returns>Previous state or null if not found</returns>
        Task<bool?> ToggleDisabled(string domain, bool disabled);

        /// <summary>
        /// Marks an account for deletion as of now(); returns the date on which it will be deleted based on registry config
        /// </summary>
        Task<UnixTimeUtc> MarkForDeletionAsync(string domain);

        Task UnmarkForDeletionAsync(string domain);
    }
}