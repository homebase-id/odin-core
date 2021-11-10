using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine.Filter
{
    public class IsAppAllowedFilter : ITransitStreamFilter
    {
        public Guid Id => Guid.Parse("20000000-1111-0000-1111-222AAADDD000");

        public Task<FilterResult> Apply(IFilterContext context, FilePart part, Stream data)
        {
            //TODO: check check the the authorized app list

            var blacklist = new List<Guid>()
            {
                Guid.Empty
            };

            if (blacklist.Contains(context.AppId))
            {
                return Task.FromResult(new FilterResult()
                {
                    FilterId = this.Id,
                    Recommendation = FilterAction.Accept
                });
            }
            
            var result = new FilterResult()
            {
                FilterId = this.Id,
                Recommendation = FilterAction.Accept
            };

            return Task.FromResult(result);
        }
    }
}