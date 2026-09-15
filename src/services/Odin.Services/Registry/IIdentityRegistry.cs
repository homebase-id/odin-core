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
        /// Starts receiving registry changes made by other nodes. Call before
        /// <see cref="LoadRegistrations"/>; the handler tolerates a change arriving mid-load.
        /// </summary>
        Task SubscribeToRegistryChangesAsync();

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
        
        Task AssetValidFirstRunToken(Guid firstRunToken, IOdinContext odinContext);

        /// <summary>
        /// Returns the registration status for the identity
        /// </summary>
        Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken);

        /// <summary>
        /// Sets the identity's <see cref="TenantStatus"/> and, before returning, brings this node's background services
        /// in line with it: stopped when paused or disabled, running otherwise. Other nodes follow once they apply the change. A disabled status without a reason means
        /// <see cref="DisabledReason.Admin"/>. Throws <see cref="Odin.Core.Exceptions.OdinClientException"/> on a
        /// transition <see cref="TenantStatusRules.Validate"/> refuses.
        /// </summary>
        /// <returns>Previous state or null if not found</returns>
        Task<TenantStatusState> SetStatusAsync(string domain, TenantStatus status, DisabledReason? reason = null);

        /// <summary>
        /// Sets whether the identity is allowed a public home page
        /// </summary>
        /// <returns>Previous state or null if not found</returns>
        Task<bool?> SetPublicWebPresenceAsync(string domain, bool enabled);

        /// <summary>
        /// Marks an account for deletion as of now(); returns the date on which it will be deleted based on registry config
        /// </summary>
        Task<UnixTimeUtc> MarkForDeletionAsync(string domain);

        Task UnmarkForDeletionAsync(string domain);
    }
}