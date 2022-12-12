using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Holds contextual information needed by instances of <see cref="ITransitStreamFilter"/>
    /// </summary>
    public interface IFilterContext
    {
        DotYouIdentity Sender { get; init; }
        
        //TODO: what else is needed here?
    }
}