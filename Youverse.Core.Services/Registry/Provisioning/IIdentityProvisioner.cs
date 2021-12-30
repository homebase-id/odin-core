using System.Threading.Tasks;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Registry.Provisioning
{
    /// <summary>
    /// Sets up all the defaults for a DotYouIdentity
    /// </summary>
    public interface IIdentityProvisioner
    {
        /// <summary>
        /// Setups the default items for a new identity (apps, drives, etc.)
        /// </summary>
        Task ConfigureIdentityDefaults();
    }
}