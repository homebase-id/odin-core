using System.Threading.Tasks;
using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Core.Services.Profile
{
    /// <summary>
    /// Enables the owner of a DI to read/write their data attributes  This handles both fixed
    /// and generic attributes.  (Fixed attributes are those built-into the system like
    /// Name, Address, etc.)
    ///
    /// This is not intended to be consumed by any services on the perimeter.
    /// </summary>
    public interface IOwnerDataAttributeManagementService : IDataAttributeManagementService
    {
        /// <summary>
        /// Sets the profile information available to the public internet
        /// </summary>
        Task SavePublicProfile(params BaseAttribute[] attributes);

        Task SaveConnectedProfile(params BaseAttribute[] attributes);

        /// <summary>
        /// Returns the most basic information for a public profile.  Essentially it's name, photo, and anything else you use often.
        /// </summary>
        Task<BasicProfileInfo> GetBasicPublicProfile();

        /// <summary>
        /// Returns the most basic information for a connected profile.  Essentially it's name, photo, and anything else you use often.
        /// </summary>
        Task<BasicProfileInfo> GetBasicConnectedProfile();
        
        Task<PagedResult<BaseAttribute>> GetPublicProfileAttributeCollection(PageOptions pageOptions);

        Task<PagedResult<BaseAttribute>> GetConnectedProfileAttributeCollection(PageOptions pageOptions);
    }
}