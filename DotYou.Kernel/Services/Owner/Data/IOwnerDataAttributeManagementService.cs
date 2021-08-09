using System.Threading.Tasks;
using DotYou.Kernel.Services.DataAttribute;
using DotYou.Types;
using DotYou.Types.DataAttribute;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Enables a Digital Identity Owner to manage their identity
    /// data, including who can see what.
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