using System;
using System.IO;
using System.Threading.Tasks;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;

namespace Odin.Core.Services.Transit.ReceivingHost.Quarantine.Filter
{
    public class MustBeConnectedOrDataProviderFilter : ITransitStreamFilter
    {
        private readonly OdinContextAccessor _contextAccessor;

        public MustBeConnectedOrDataProviderFilter(OdinContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        public Guid Id => Guid.Parse("00000000-0000-0000-833e-8bf7bcf62478");

        public Task<FilterResult> Apply(IFilterContext context, MultipartHostTransferParts part, Stream data)
        {
            var dotYouContext = _contextAccessor.GetCurrent();
            if (dotYouContext.Caller.IsConnected || dotYouContext.Caller.ClientTokenType == ClientTokenType.DataProvider)
            {
                return Task.FromResult(new FilterResult()
                {
                    FilterId = this.Id,
                    Recommendation = FilterAction.Accept
                });
            }

            return Task.FromResult(new FilterResult()
            {
                FilterId = this.Id,
                Recommendation = FilterAction.Reject
            });
        }
    }
}