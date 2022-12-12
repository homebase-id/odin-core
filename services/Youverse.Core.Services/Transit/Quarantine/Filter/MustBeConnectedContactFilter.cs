using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Quarantine.Filter
{
    public class MustBeConnectedContactFilter : ITransitStreamFilter
    {
        public Guid Id => Guid.Parse("00000000-0000-0000-833e-8bf7bcf62478");

        public Task<FilterResult> Apply(IFilterContext context, MultipartHostTransferParts part, Stream data)
        {
            var result = new FilterResult()
            {
                FilterId = this.Id,
                Recommendation = FilterAction.Accept
            };

            return Task.FromResult(result);
        }
    }
}