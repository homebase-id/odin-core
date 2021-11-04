using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;
using Youverse.Core.Services.Transit.Audit;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterService : TransitServiceBase, ITransitPerimeterService
    {
        private struct PartState
        {
            /// <summary>
            /// Specifies the part has been provided to the perimeter service
            /// </summary>
            public bool IsAcquired;

            /// <summary>
            /// Specifies the result of the filter applied to the part
            /// </summary>
            public FilterAction FilterResult;

            public bool IsValid()
            {
                return IsAcquired && FilterResult == FilterAction.Accept;
            }

            public bool IsRejected()
            {
                return this.IsAcquired && this.FilterResult == FilterAction.Reject;
            }

            public bool IsQuarantined()
            {
                return this.IsAcquired && this.FilterResult == FilterAction.Quarantine;
            }

            public void SetValid()
            {
                this.FilterResult = FilterAction.Accept;
                this.IsAcquired = true;
            }
        }

        private class FileTracker
        {
            public FileTracker(Guid id)
            {
                Guard.Argument(id, nameof(id)).NotEqual(Guid.Empty);
                this.Id = id;
            }

            public Guid Id { get; init; }

            public PartState HeaderState;
            public PartState MetadataState;
            public PartState PayloadState;

            public void SetAccepted(FilePart part)
            {
                switch (part)
                {
                    case FilePart.Header:
                        HeaderState.SetValid();
                        break;

                    case FilePart.Metadata:
                        MetadataState.SetValid();
                        break;

                    case FilePart.Payload:
                        PayloadState.SetValid();
                        break;
                }
            }

            /// <summary>
            /// Indicates if the marker has at least one part that's been rejected by a filter
            /// </summary>
            public bool HasAcquiredRejectedPart()
            {
                return HeaderState.IsRejected() || MetadataState.IsRejected() || PayloadState.IsRejected();
            }

            /// <summary>
            /// Indicates if the marker has at least one part that's been quarantined by a filter
            /// </summary>
            public bool HasAcquiredQuarantinedPart()
            {
                return HeaderState.IsQuarantined() || MetadataState.IsQuarantined() || PayloadState.IsQuarantined();
            }

            public bool IsCompleteAndValid()
            {
                return this.HeaderState.IsValid() && MetadataState.IsValid() && PayloadState.IsValid();
            }
        }

        private readonly ITransitService _transitService;
        private readonly IStorageService _fileStorage;
        private readonly IDictionary<Guid, FileTracker> _fileTrackers;
        private readonly ITransitQuarantineService _quarantineService;

        public TransitPerimeterService(DotYouContext context, ILogger logger, ITransitAuditWriterService auditWriter, ITransitService transitService, ITransitQuarantineService quarantineService, IStorageService fileStorage) : base(context, logger, auditWriter, null, null)
        {
            _transitService = transitService;
            _quarantineService = quarantineService;
            _fileStorage = fileStorage;
            _fileTrackers = new Dictionary<Guid, FileTracker>();
        }

        public async Task<Guid> CreateFileTracker()
        {
            var id = await this.AuditWriter.CreateAuditTrackerId();
            _fileTrackers.Add(id, new FileTracker(id));
            return id;
        }

        public async Task<AddPartResponse> FilterFilePart(Guid fileId, FilePart part, Stream data)
        {
            var tracker = GetTrackerOrFail(fileId);

            if (tracker.HasAcquiredRejectedPart())
            {
                throw new InvalidDataException("Corresponding part has been rejected");
            }

            if (tracker.HasAcquiredQuarantinedPart())
            {
                //quarantine the rest
                return await QuarantinePart(tracker, part, data);
            }

            var filterResponse = await _quarantineService.ApplyFilters(tracker.Id, part, data);

            switch (filterResponse.Code)
            {
                case FinalFilterAction.Accepted:
                    return await AcceptPart(tracker, part, data);

                case FinalFilterAction.QuarantinedPayload:
                case FinalFilterAction.QuarantinedSenderNotConnected:
                    return await QuarantinePart(tracker, part, data);

                case FinalFilterAction.Rejected:
                default:
                    return RejectPart(tracker, part, data);
            }
        }

        public bool IsFileValid(Guid fileId)
        {
            return _fileTrackers[fileId].IsCompleteAndValid();
        }

        public Task<CollectiveFilterResult> GetFinalFilterResult(Guid fileId)
        {
            var marker = GetTrackerOrFail(fileId);

            //so the final result will be either it was quarantined or accepted
            //if it was quarantined, we we want to say why so the sender can be told
            //how do i know which filter quarantined it?? i suppose that's the questiln

            //i woudl rather have the filter give a reason that maps to the system rather than know which filter
            //did the quarantining..  if there is a custom message the fitler can maybe send back a string.

            //so i need a global set of codes in transit that must be returned by a fitler

            //TODO: 
            var result = new CollectiveFilterResult()
            {
                Code = FinalFilterAction.Accepted,
                Message = ""
            };

            return Task.FromResult(result);
        }

        private FileTracker GetTrackerOrFail(Guid fileId)
        {
            if (!_fileTrackers.TryGetValue(fileId, out var marker))
            {
                throw new InvalidDataException("Invalid file Id");
            }

            return marker;
        }

        private AddPartResponse RejectPart(FileTracker tracker, FilePart part, Stream data)
        {
            this.AuditWriter.WriteEvent(tracker.Id, TransitAuditEvent.Rejected);
            //do nothing with the stream since it's bad
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Reject
            };
        }

        private async Task<AddPartResponse> QuarantinePart(FileTracker tracker, FilePart part, Stream data)
        {
            await _quarantineService.Quarantine(tracker.Id, part, data);
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Quarantine,
            };
        }

        private async Task<AddPartResponse> AcceptPart(FileTracker tracker, FilePart part, Stream data)
        {
            data.Position = 0;
            tracker.SetAccepted(part);
            
            //write the part to long term storage but it will not be complete or accessible until we
            //tell the transit service to complete the transfer
            
            //_fileStorage.WritePartStream()
            //_transitService.Accept(tracker.Id);

            //triage, decrypt, route the payload
            var result = new AddPartResponse()
            {
                FilterAction = FilterAction.Accept
            };

            return result;
        }
    }
}