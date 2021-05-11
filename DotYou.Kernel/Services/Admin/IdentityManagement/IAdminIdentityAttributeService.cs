using System.Threading.Tasks;
using DotYou.Types.Identity;
using Identity.DataType.Attributes;

namespace DotYou.Kernel.Services.Admin.IdentityManagement
{
    /// <summary>
    /// Enables a Digital Identity Owner to manage their identity data, including
    /// the values as well as who can see what.
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
    }
}