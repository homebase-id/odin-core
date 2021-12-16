using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Container
{
    public class ContainerResolver : IContainerResolver
    {
        public Task<ContainerInfo> Resolve(Guid containerId)
        {
            var container = new ContainerInfo()
            {
                ContainerId = containerId,
                IndexName = $"{containerId}-idx"
            };

            return Task.FromResult(container);
        }

        public Task<PagedResult<ContainerInfo>> GetContainers(PageOptions pageOptions)
        {
            //TODO:looks these up somewhere
            var page = new PagedResult<ContainerInfo>()
            {
                Request = pageOptions,
                Results = new List<ContainerInfo>()
                {
                    new ContainerInfo()
                    {
                        ContainerId = Guid.Parse("11111234-2931-4fa1-0000-CCCC40000001")
                    }
                },
                TotalPages = 1
            };

            return Task.FromResult(page);
        }
    }
}