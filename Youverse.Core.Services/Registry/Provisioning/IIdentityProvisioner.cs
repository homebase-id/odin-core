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
        /// Ensures all system apps are created, including new ones introduced to existing tenants (i.e. call this once at startup for each tenant)
        /// </summary>
        Task EnsureSystemApps();
    }
}