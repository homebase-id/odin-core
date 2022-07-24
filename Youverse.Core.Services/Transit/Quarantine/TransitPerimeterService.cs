using Dawn;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.EncryptionKeyService;
using Youverse.Core.Services.Transit.Quarantine.Filter;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterService : TransitServiceBase<ITransitPerimeterService>, ITransitPerimeterService
    {
        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ITransitService _transitService;
        private readonly IAppRegistrationService _appRegService;
        private readonly ITransitPerimeterTransferStateService _transitPerimeterTransferStateService;
        private readonly IPublicKeyService _publicKeyService;

        public TransitPerimeterService(
            DotYouContextAccessor contextAccessor,
            ILogger<ITransitPerimeterService> logger,
            ITransitService transitService,
            IAppRegistrationService appRegService,
            ITransitPerimeterTransferStateService transitPerimeterTransferStateService, IPublicKeyService publicKeyService) : base()
        {
            _contextAccessor = contextAccessor;
            _transitService = transitService;
            _appRegService = appRegService;
            _transitPerimeterTransferStateService = transitPerimeterTransferStateService;
            _publicKeyService = publicKeyService;
        }

        public async Task<Guid> InitializeIncomingTransfer(RsaEncryptedRecipientTransferInstructionSet transferInstructionSet)
        {
            Guard.Argument(transferInstructionSet, nameof(transferInstructionSet)).NotNull();
            Guard.Argument(transferInstructionSet!.PublicKeyCrc, nameof(transferInstructionSet.PublicKeyCrc)).NotEqual<uint>(0);
            Guard.Argument(transferInstructionSet.EncryptedAesKeyHeader.Length, nameof(transferInstructionSet.EncryptedAesKeyHeader)).NotEqual(0);

            if (!await _publicKeyService.IsValidPublicKey(transferInstructionSet.PublicKeyCrc))
            {
                throw new TransitException("Invalid Public Key CRC provided");
            }

            return await _transitPerimeterTransferStateService.CreateTransferStateItem(transferInstructionSet);
        }

        public async Task<AddPartResponse> ApplyFirstStageFiltering(Guid transferStateItemId, MultipartHostTransferParts part, string fileExtension, Stream data)
        {
            var item = await _transitPerimeterTransferStateService.GetStateItem(transferStateItemId);

            if (item.HasAcquiredRejectedPart())
            {
                throw new HostToHostTransferException("Corresponding part has been rejected");
            }

            if (item.HasAcquiredQuarantinedPart())
            {
                //quarantine the rest
                await _transitPerimeterTransferStateService.Quarantine(item.Id, part, fileExtension, data);
            }

            var filterResponse = await ApplyFilters(part, data);

            switch (filterResponse)
            {
                case FilterAction.Accept:
                    await _transitPerimeterTransferStateService.AcceptPart(item.Id, part, fileExtension, data);
                    break;

                case FilterAction.Quarantine:
                    await _transitPerimeterTransferStateService.Quarantine(item.Id, part, fileExtension, data);
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
                return new HostTransferResponse() { Code = TransitResponseCode.QuarantinedPayload };
            }

            if (item.HasAcquiredRejectedPart())
            {
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransferResponse() { Code = TransitResponseCode.Rejected };
            }

            if (item.IsCompleteAndValid())
            {
                await _transitService.AcceptTransfer(item.TempFile, item.PublicKeyCrc);
                await _transitPerimeterTransferStateService.RemoveStateItem(item.Id);
                return new HostTransferResponse() { Code = TransitResponseCode.Accepted };
            }

            throw new HostToHostTransferException("Unhandled error");
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
                Sender = this._contextAccessor.GetCurrent().Caller.DotYouId
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