using System;
using Youverse.Core.Identity;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class FilterContext : IFilterContext
    {
        public DotYouIdentity Sender { get; init; }
        public string AppId { get; init; }
    }
}