using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Handles incoming payloads at the perimeter of the DI host.  
    /// </summary>
    public interface ITransitPerimeterService
    {
        /// <summary>
        /// Prepares a holder for an incoming file and returns the Id.  You should use this Id on calls to <see cref="FilterPart"/>
        /// </summary>
        /// <returns></returns>
        Task<Guid> CreateFileTracker();

        /// <summary>
        /// Filters, Triages, and distributes the incoming payload the right handler
        /// </summary>
        /// <returns></returns>
        Task<AddPartResponse> FilterPart(Guid fileId, FilePart part, Stream data);

        /// <summary>
        /// Indicates if the file has all required parts and all parts are valid
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        bool IsFileValid(Guid fileId);
        
        /// <summary>
        /// Gets the final result after filters applied to all incoming parts of the file
        /// </summary>
        /// <param name="fileId"></param>
        Task<CollectiveFilterResult> GetFinalFilterResult(Guid fileId);
    }
}