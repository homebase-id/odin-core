using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.ReceivingHost.Quarantine
{
    public class FilterContext : IFilterContext
    {
        public OdinId Sender { get; init; }
    }
}