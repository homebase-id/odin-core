using System;
using System.IO;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Enables the staging of outgoing data for a given tenant before it transferred.  This handles
    /// scenarios where large uploads take time before all parts are ready to be processed (i.e. it bundles multipart
    /// uploads into into a <see cref="Transfer"/>
    /// </summary>
    public interface IMultipartPackageStorageWriter
    {
        /// <summary>
        /// Prepares an item to be collected and returns an Id you will use to send parts of an upload as they are received.
        /// </summary>
        /// <returns></returns>
        Task<Guid> CreatePackage();

        /// <summary>
        /// Accepts a part of a Multipart stream.  When all required parts are received
        /// </summary>
        /// <param name="pkgId"></param>
        /// <param name="name"></param>
        /// <param name="data"></param>
        /// <returns>True when all parts are received, otherwise false</returns>
        Task<bool> AddPart(Guid pkgId, string name, Stream data);

        /// <summary>
        /// Gets the <see cref="UploadPackage"/>
        /// </summary>
        /// <param name="packageId"></param>
        /// <returns></returns>
        Task<UploadPackage> GetPackage(Guid packageId);
    }
}