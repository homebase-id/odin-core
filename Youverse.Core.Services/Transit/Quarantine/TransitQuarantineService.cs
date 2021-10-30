using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitQuarantineService : DotYouServiceBase, ITransitQuarantineService
    {
        private readonly IStorageService _storage;

        public TransitQuarantineService(DotYouContext context, ILogger logger, IStorageService storage) : base(context, logger, null, null)
        {
            _storage = storage;
        }

        public async Task<CollectiveFilterResult> ApplyFilters(FilePart part, Stream data)
        {
            //TODO: when this has the full set of filters
            // applied, we need to spawn into multiple
            // threads/tasks so we don't cause a long delay
            // of deciding on incoming data

            //TODO: will need to come from a configuration list
            var filters = new List<ITransitStreamFilter>()
            {
                new MustBeConnectedContactFilter()
            };

            var context = new FilterContext()
            {
                Sender = this.Context.Caller.DotYouId
            };
            
            //TODO: this should be executed in parallel
            foreach (var filter in filters)
            {
                var result = await filter.Apply(context, part, data);

                //TODO: here we can check additional aspects of the filter to
                //determine if we want to immediately follow it's recommendation.
                //short circuit immediately
                if (result.Recommendation == FilterAction.Reject)
                {
                    return new CollectiveFilterResult()
                    {
                        Code = FinalFilterAction.Rejected
                    };
                }
            }

            return new CollectiveFilterResult()
            {
                Code = FinalFilterAction.Accepted,
                Message = ""
            };
        }

        public async Task Quarantine(FilePart part, Stream data)
        {
            throw new System.NotImplementedException();
        }
    }
}