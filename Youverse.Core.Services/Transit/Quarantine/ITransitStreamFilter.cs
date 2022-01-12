using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Defines a filter capable of accepting, quarantining, or rejecting in coming payloads
    /// </summary>
    public interface ITransitStreamFilter
    {
        /// <summary>
        /// The identifier for the filter
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Apply the filter to the incoming <param name="data"></param>
        /// </summary>
        /// <param name="context">Contextual information for this filter</param>
        /// <param name="part">The <see cref="FilePart"/> being processed by the filter</param>
        /// <param name="data">The stream of data to be processed</param>
        /// <returns>A <see cref="FilterResult"/> indicating the result of the filter</returns>
        Task<FilterResult> Apply(IFilterContext context, MultipartHostTransferParts part, Stream data);
    }
}