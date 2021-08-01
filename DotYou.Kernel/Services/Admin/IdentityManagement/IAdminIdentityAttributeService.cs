using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Identity;
using Identity.DataType.Attributes;

namespace DotYou.Kernel.Services.Admin.IdentityManagement
{
    /// <summary>
    /// Enables a Digital Identity Owner to manage their identity
    /// data, including who can see what.
    /// </summary>
    public interface IAdminIdentityAttributeService
    {
        /// <summary>
        /// Returns a <see cref="NameAttribute"/> for this Digital Identity's primary name.
        /// </summary>
        /// <returns></returns>
        Task<NameAttribute> GetPrimaryName();

        /// <summary>
        /// Sets the primary name for this Digital Identity
        /// </summary>
        /// <param name="name"></param>
        Task SavePrimaryName(NameAttribute name);

        /// <summary>
        /// Retrieves the profile information available to the public internet
        /// </summary>
        /// <returns></returns>
        Task<PublicProfile> GetPublicProfile();

        /// <summary>
        /// Sets the profile information available to the public internet
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task SavePublicProfile(PublicProfile profile);
    }
}