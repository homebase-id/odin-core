using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Resolves information about a container.
    /// </summary>
    public interface IDriveResolver
    {
        Task<StorageDrive> Resolve(Guid driveId);

        /// <summary>
        /// Returns a list of the containers in the system
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions);

        /// <summary>
        /// Gets the latest status for a <see cref="StorageDrive"/>
        /// </summary>
        Task<StorageDriveStatus> ResolveStatus(Guid driveId);
    }
}