using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Handles incoming payloads at the perimeter of the DI host.  
    /// </summary>
    public interface ITransitPerimeterService
    {
        /// <summary>
        /// Prepares a holder for an incoming file and returns the Id.  You should use this Id on calls to <see cref="AddPart"/>
        /// </summary>
        /// <returns></returns>
        Task<Guid> StartIncomingFile();

        /// <summary>
        /// Filters, Triages, and distributes the incoming payload the right handler
        /// </summary>
        /// <returns></returns>
        Task<AddPartResponse> AddPart(Guid fileId, FilePart part, Stream data);

        bool IsFileComplete(Guid fileId);
    }
    
    public class TransitPerimeterService : DotYouServiceBase, ITransitPerimeterService
    {
        private class FileMarker
        {
            public bool HasValidHeader;
            public bool HasValidMetadata;
            public bool HasValidPayload;

            public void SetValid(FilePart part)
            {
                switch (part)
                {
                    case FilePart.Header:
                        HasValidHeader = true;
                        break;

                    case FilePart.Metadata:
                        HasValidMetadata = true;
                        break;

                    case FilePart.Payload:
                        HasValidPayload = true;
                        break;
                }
            }

            public bool IsComplete()
            {
                return HasValidHeader && HasValidMetadata && HasValidPayload;
            }
        }

        private readonly IDictionary<Guid, FileMarker> _fileMarkers;
        private readonly ITransitService _transitService;
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

        public async Task<AddPartResponse> AddPart(Guid fileId, FilePart part, Stream data)
        {
            // if (!_fileMarkers.TryGetValue(fileId, out var marker))
            // {
            //     throw new InvalidDataException("Invalid file Id");
            // }
            //
            // var filterResponse = await _quarantineService.ApplyFilters(part, data);
            //
            // if (filterResponse.SuggestedAction == SuggestedAction.None)
            // {
            //     data.Position = 0;
            //     marker.SetValid(part);
            //     
            //     //triage, decrypt, route the payload
            //     
            //
            // }
            // else
            // {
            //     if (filterResponse.SuggestedAction == SuggestedAction.Reject)
            //     {
            //         new AddPartResponse()
            //         {
            //             
            //         }
            //         
            //     }
            //
            //     if (filterResponse.SuggestedAction == SuggestedAction.Quarantine)
            //     {
            //         
            //     }
            //     
            //     //if the data was quarantined, we can tell the sender that the
            //     //data was not accepted, and potentially say the reason why
            // }
            return null;
        }

        public bool IsFileComplete(Guid fileId)
        {
            return _fileMarkers[fileId].IsComplete();
        }
    }
}