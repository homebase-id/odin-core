using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Container.Query
{
    /// <summary>
    /// Indexes the metadata of one or more containers
    /// </summary>
    public interface IContainerMetadataIndexer
    {
        /// <summary>
        /// Rebuilds the full index from start
        /// </summary>
        Task Rebuild();

        
        /// <summary>
        /// Rebuilds the index for the specified container
        /// </summary>
        Task Rebuild(Guid containerId);
    }
}