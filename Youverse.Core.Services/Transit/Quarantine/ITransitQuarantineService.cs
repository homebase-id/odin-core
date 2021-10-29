using System.IO;
using System.Threading.Tasks;
using Refit;
using Youverse.Core.Services.Storage;

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
        Task<FilterResponse> ApplyFilters(FilePart part, Stream data);
    }
}