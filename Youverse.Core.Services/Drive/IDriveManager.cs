using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Resolves information about a container.
    /// </summary>
    public interface IDriveManager
    {
        Task<StorageDrive> GetDrive(Guid driveId);

        /// <summary>
        /// Returns a list of the containers in the system
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions);
    }
}