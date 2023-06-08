using Odin.Core.Identity;

namespace Odin.Core.Services.Transit.ReceivingHost.Quarantine
{
    /// <summary>
    /// Holds contextual information needed by instances of <see cref="ITransitStreamFilter"/>
    /// </summary>
    public interface IFilterContext
    {
        OdinId Sender { get; init; }
        
        //TODO: what else is needed here?
    }
}