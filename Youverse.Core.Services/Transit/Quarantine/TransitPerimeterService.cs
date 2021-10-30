using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    public class TransitPerimeterService : DotYouServiceBase, ITransitPerimeterService
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

        private class FileMarker
        {
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
        private readonly IDictionary<Guid, FileMarker> _fileMarkers;
        private readonly ITransitQuarantineService _quarantineService;

        public TransitPerimeterService(DotYouContext context, ILogger logger, ITransitService transitService, ITransitQuarantineService quarantineService) : base(context, logger, null, null)
        {
            _transitService = transitService;
            _quarantineService = quarantineService;
            _fileMarkers = new Dictionary<Guid, FileMarker>();
        }

        public Task<Guid> StartIncomingFile()
        {
            var id = Guid.NewGuid();
            _fileMarkers.Add(id, new FileMarker());
            return Task.FromResult(id);
        }

        public async Task<AddPartResponse> FilterPart(Guid fileId, FilePart part, Stream data)
        {
            var marker = GetMarkerOrFail(fileId);

            if (marker.HasAcquiredRejectedPart())
            {
                throw new InvalidDataException("Corresponding part has been rejected");
            }

            if (marker.HasAcquiredQuarantinedPart())
            {
                //quarantine the rest
                return await QuarantinePart(marker, part, data);
            }

            var filterResponse = await _quarantineService.ApplyFilters(part, data);

            switch (filterResponse.Code)
            {
                case FinalFilterAction.Accepted:
                    return await AcceptPart(marker, part, data);

                case FinalFilterAction.QuarantinedPayload:
                case FinalFilterAction.QuarantinedSenderNotConnected:
                    return await QuarantinePart(marker, part, data);

                case FinalFilterAction.Rejected:
                default:
                    return RejectPart(marker, part, data);
            }
        }

        public bool IsFileValid(Guid fileId)
        {
            return _fileMarkers[fileId].IsCompleteAndValid();
        }

        public Task<CollectiveFilterResult> GetFinalFilterResult(Guid fileId)
        {
            var marker = GetMarkerOrFail(fileId);

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

        private FileMarker GetMarkerOrFail(Guid fileId)
        {
            if (!_fileMarkers.TryGetValue(fileId, out var marker))
            {
                throw new InvalidDataException("Invalid file Id");
            }

            return marker;
        }

        private AddPartResponse RejectPart(FileMarker marker, FilePart part, Stream data)
        {
            //do nothing with the stream since it's bad
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Reject
            };
        }

        private async Task<AddPartResponse> QuarantinePart(FileMarker marker, FilePart part, Stream data)
        {
            await _quarantineService.Quarantine(part, data);
            return new AddPartResponse()
            {
                FilterAction = FilterAction.Quarantine,
            };
        }

        private async Task<AddPartResponse> AcceptPart(FileMarker marker, FilePart part, Stream data)
        {
            data.Position = 0;
            marker.SetAccepted(part);

            //where to store?

            //triage, decrypt, route the payload
            var result = new AddPartResponse()
            {
                FilterAction = FilterAction.Accept
            };

            return result;
        }
    }
}