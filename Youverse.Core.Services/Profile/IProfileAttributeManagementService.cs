using System;
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
    public interface IProfileAttributeManagementService : IDataAttributeManagementService
    {
        /// <summary>
        /// Sets the profile information available to the public internet
        /// </summary>
        Task SavePublicProfile(NameAttribute primaryName, ProfilePicAttribute photo, params BaseAttribute[] additionalAttributes);

        Task SaveConnectedProfile(NameAttribute primaryName, ProfilePicAttribute photo, params BaseAttribute[] additionalAttributes);

        /// <summary>
        /// Returns the most basic information for a public profile.  Essentially it's name, photo, and anything else you use often.
        /// </summary>
        Task<BasicProfileInfo> GetBasicPublicProfile();

        /// <summary>
        /// Returns the most basic information for a connected profile.  Essentially it's name, photo, and anything else you use often.
        /// </summary>
        /// <param name="fallbackToEmpty">An empty profile is returned if true; otherwise null is returned</param>
        /// <returns></returns>
        Task<BasicProfileInfo> GetBasicConnectedProfile(bool fallbackToEmpty = false);
    }
}