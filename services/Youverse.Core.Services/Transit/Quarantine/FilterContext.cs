using System;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class FilterContext : IFilterContext
    {
        public DotYouIdentity Sender { get; init; }
    }
}