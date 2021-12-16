using System;
using System.IO;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Handles paylods/data that does not pass the perimeter filters.
    /// </summary>
    public interface ITransitQuarantineService
    {
        /// <summary>
        /// Applies the configured filters to an incoming transfer.  If all filters pass, the
        /// item is handed to the transit service for further processing; otherwise it is quarantined
        /// </summary>
        /// <param name="trackerId">The Id used when auditing the result of each filtered applied to the incoming transfer</param>
        /// <param name="part"></param>
        /// <param name="data"></param>
        Task<CollectiveFilterResult> ApplyFirstStageFilters(Guid trackerId, FilePart part, Stream data);

        /// <summary>
        /// Writes the stream to a quarantined location
        /// </summary>
        /// <param name="trackerId">The Id used when auditing an incoming transfer</param>
        /// <param name="part"></param>
        /// <param name="data"></param>
        Task QuarantinePart(Guid trackerId, FilePart part, Stream data);


        /// <summary>
        /// Quarantines the specified file
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task Quarantine(Guid fileId);
    }
}