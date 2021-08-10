using System.Threading.Tasks;
using DotYou.Kernel.Services.DataAttribute;
using DotYou.Types;
using DotYou.Types.DataAttribute;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Enables the owner of a DI to read/write their data attributes  This handles both fixed
    /// and generic attributes.  (Fixed attributes are those built-into the system like
    /// Name, Address, etc.)
    ///
    /// This is not intended to be consumed by any services on the perimeter.
    /// </summary>
    public interface IOwnerDataAttributeManagementService: IDataAttributeManagementService
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
        /// Sets the profile information available to the public internet
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task SavePublicProfile(Profile profile);
        
        Task SaveConnectedProfile(ConnectedProfile profile);
        
    }
}