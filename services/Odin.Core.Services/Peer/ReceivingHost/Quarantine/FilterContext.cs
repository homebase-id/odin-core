using Odin.Core.Identity;

namespace Odin.Core.Services.Transit.ReceivingHost.Quarantine
{
    public class FilterContext : IFilterContext
    {
        public OdinId Sender { get; init; }
    }
}