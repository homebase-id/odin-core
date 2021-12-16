using System;

namespace Youverse.Core.Services.Container
{
    /// <summary>
    /// Information about a container
    /// </summary>
    public class ContainerInfo
    {
        public Guid ContainerId { get; init; }
        
        public string IndexName { get; init; }
    }
}