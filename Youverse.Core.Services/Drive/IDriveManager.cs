using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Resolves information about a container.
    /// </summary>
    public interface IDriveManager
    {
        /// <summary>
        /// Creates a new storage drive
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        Task<StorageDrive> CreateDrive(string name);
        
        Task<StorageDrive> GetDrive(Guid driveId, bool failIfInvalid = false);

        /// <summary>
        /// Returns a list of the containers in the system
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions);
    }
}