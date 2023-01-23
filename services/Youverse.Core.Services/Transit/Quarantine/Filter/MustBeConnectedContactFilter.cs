using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Quarantine.Filter
{
    public class MustBeConnectedContactFilter : ITransitStreamFilter
    {
        private readonly DotYouContextAccessor _contextAccessor;

        public MustBeConnectedContactFilter(DotYouContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        public Guid Id => Guid.Parse("00000000-0000-0000-833e-8bf7bcf62478");

        public Task<FilterResult> Apply(IFilterContext context, MultipartHostTransferParts part, Stream data)
        {
            if (!_contextAccessor.GetCurrent().Caller.IsConnected)
            {
                return Task.FromResult(new FilterResult()
                {
                    FilterId = this.Id,
                    Recommendation = FilterAction.Reject
                });
            }

            return Task.FromResult(new FilterResult()
            {
                FilterId = this.Id,
                Recommendation = FilterAction.Accept
            });
        }
    }
}