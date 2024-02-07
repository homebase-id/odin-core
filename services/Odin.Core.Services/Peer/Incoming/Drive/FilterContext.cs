using Odin.Core.Identity;

namespace Odin.Core.Services.Peer.Incoming.Drive
{
    public class FilterContext : IFilterContext
    {
        public OdinId Sender { get; init; }
    }
}