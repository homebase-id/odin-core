using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Container
{
    /// <summary>
    /// Resolves information about a container.
    /// </summary>
    public interface IContainerResolver
    {
        Task<ContainerInfo> Resolve(Guid containerId);

        /// <summary>
        /// Returns a list of the containers in the system
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<ContainerInfo>> GetContainers(PageOptions pageOptions);
    }
}