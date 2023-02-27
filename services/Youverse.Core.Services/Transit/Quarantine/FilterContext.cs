using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class FilterContext : IFilterContext
    {
        public OdinId Sender { get; init; }
    }
}