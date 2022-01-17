using Dawn;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Transit.Audit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Transit.Quarantine.Filter;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterService : TransitServiceBase<ITransitPerimeterService>, ITransitPerimeterService
    {
        private readonly DotYouContext _context;
        private readonly ITransitService _transitService;
        private readonly IAppRegistrationService _appRegService;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;

        public TransitPerimeterService(
            DotYouContext context,
            ILogger<ITransitPerimeterService> logger,
            ITransitAuditWriterService auditWriter,
            ITransitService transitService,
            IAppRegistrationService appRegService,
            ITransitPerimeterTransferStateService transitPerimeterTransferStateService) : base(auditWriter)
        {
            _context = context;
            _transitService = transitService;
            _appRegService = appRegService;
            _transitPerimeterTransferStateService = transitPerimeterTransferStateService;
        }

        public async Task<Guid> InitializeIncomingTransfer(RsaEncryptedRecipientTransferKeyHeader rsaKeyHeader)
        {
            Guard.Argument(rsaKeyHeader, nameof(rsaKeyHeader)).NotNull();
            Guard.Argument(rsaKeyHeader!.PublicKeyCrc, nameof(rsaKeyHeader.PublicKeyCrc)).NotEqual<uint>(0);
            Guard.Argument(rsaKeyHeader.EncryptedAesKey.Length, nameof(rsaKeyHeader.EncryptedAesKey.Length)).NotEqual(0);

            if (!await _appRegService.IsValidPublicKey(_context.TransitContext.AppId, rsaKeyHeader.PublicKeyCrc))
            {
                throw new TransitException("Invalid Public Key CRC provided");
            }

            return await _transitPerimeterTransferStateService.CreateTransferStateItem(rsaKeyHeader);
        }

        public async Task<AddPartResponse> ApplyFirstStageFiltering(Guid transferStateItemId, MultipartHostTransferParts part, Stream data)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            if (item.HasAcquiredRejectedPart())
            {
                throw new HostToHostTransferException("Corresponding part has been rejected");
            }

            if (item.HasAcquiredQuarantinedPart())
            {
                //quarantine the rest
                await _transitPerimeterTransferStateService.Quarantine(item.Id, part, data);
            }

            var filterResponse = await ApplyFilters(part, data);

            switch (filterResponse)
            {
                case FilterAction.Accept:
                    await _transitPerimeterTransferStateService.AcceptPart(item.Id, part, data);
                    break;

                case FilterAction.Quarantine:
                    await _transitPerimeterTransferStateService.Quarantine(item.Id, part, data);
                    break;

                case FilterAction.Reject:
                default:
                    await _transitPerimeterTransferStateService.Reject(item.Id, part);
                    break;
            }

            return new AddPartResponse()
            {
                FilterAction = filterResponse
            };
        }

        public async Task<bool> IsFileValid(Guid transferStateItemId)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);
            return item.IsCompleteAndValid();
        }

        public async Task<HostTransferResponse> FinalizeTransfer(Guid transferStateItemId)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            if (item.HasAcquiredQuarantinedPart())
            {
                //TODO: how do i know which filter quarantined it??
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransferResponse() {Code = TransitResponseCode.QuarantinedPayload};
            }

            if (item.HasAcquiredRejectedPart())
            {
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransferResponse() {Code = TransitResponseCode.Rejected};
            }

            if (item.IsCompleteAndValid())
            {
                await _transitService.AcceptTransfer(item.TempFile, item.PublicKeyCrc);
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransferResponse() {Code = TransitResponseCode.Accepted};
            }

            throw new HostToHostTransferException("Unhandled error");
        }

        public async Task<TransitPublicKey> GetTransitPublicKey()
        {
            var tpk = await _appRegService.GetTransitPublicKey(_context.TransitContext.AppId);
            return tpk;
        }

        private async Task<FilterAction> ApplyFilters(MultipartHostTransferParts part, Stream data)
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
                Sender = this._context.Caller.DotYouId,
                AppId = ""
            };

            //TODO: this should be executed in parallel
            foreach (var filter in filters)
            {
                var result = await filter.Apply(context, part, data);

                if (result.Recommendation == FilterAction.Reject)
                {
                    return FilterAction.Reject;
                }
            }

            return FilterAction.Accept;
        }
    }
}