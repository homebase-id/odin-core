using System;
using System.IO;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Enables the staging of outgoing data for a given tenant before it transferred.  This handles
    /// scenarios where large uploads take time before all parts are ready to be processed
    /// </summary>
    public interface IMultipartUploadQueue
    {
        /// <summary>
        /// Prepares an item to be collected and returns an Id you will use to send parts of an upload as they are received.
        /// </summary>
        /// <returns></returns>
        Task<Guid> CreatePackage();

        /// <summary>
        /// Accepts a part of a Multipart stream.  When all required parts are received
        /// </summary>
        /// <param name="packageId"></param>
        /// <param name="name"></param>
        /// <param name="payload"></param>
        /// <returns>True when all parts are received, otherwise false</returns>
        Task<bool> AcceptPart(Guid packageId, string name, Stream payload);

        /// <summary>
        /// Gets the <see cref="MultipartPackage"/>
        /// </summary>
        /// <param name="packageId"></param>
        /// <returns></returns>
        Task<MultipartPackage> GetPackage(Guid packageId);
    }
}