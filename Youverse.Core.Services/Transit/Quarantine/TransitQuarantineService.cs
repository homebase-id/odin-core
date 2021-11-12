using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Quarantine.Filter;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitQuarantineService : TransitServiceBase, ITransitQuarantineService
    {
        private readonly IStorageService _storage;

        public TransitQuarantineService(DotYouContext context, ILogger logger, IStorageService storage, ITransitAuditWriterService auditWriter) : base(context, logger, auditWriter, null, null)
        {
            _storage = storage;
        }

        public async Task<CollectiveFilterResult> ApplyFirstStageFilters(Guid trackerId, FilePart part, Stream data)
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
                Sender = this.Context.Caller.DotYouId,
                AppId = ""
            };

            //TODO: this should be executed in parallel
            foreach (var filter in filters)
            {
                var result = await filter.Apply(context, part, data);
                this.AuditWriter.WriteFilterEvent(trackerId, TransitAuditEvent.FilterApplied, filter.Id, result.Recommendation);

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

            //TODO should we add the CollectiveFilterResult here?
            this.AuditWriter.WriteEvent(trackerId, TransitAuditEvent.AllFiltersApplied);

            return new CollectiveFilterResult()
            {
                Code = FinalFilterAction.Accepted,
                Message = ""
            };
        }
        
        public async Task QuarantinePart(Guid trackerId, FilePart part, Stream data)
        {
            this.AuditWriter.WriteEvent(trackerId, TransitAuditEvent.Quarantined);
            throw new System.NotImplementedException();
        }

        public Task Quarantine(Guid fileId)
        {
            throw new NotImplementedException();
        }
    }
}