using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive.Query
{
    /// <summary>
    /// Indexes the metadata of one or more containers
    /// </summary>
    public interface IDriveMetadataIndexer
    {
        /// <summary>
        /// Rebuilds the indexes for all configured drives.
        /// </summary>
        Task RebuildAllIndices();

        
        /// <summary>
        /// Rebuilds the index for the specified drive
        /// </summary>
        Task RebuildIndex(Guid driveId);
    }
}