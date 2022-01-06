using System;
using System.IO;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// Enables the staging of outgoing data for a given tenant before it transferred.  This handles
    /// scenarios where large uploads take time before all parts are ready to be processed
    /// </summary>
    public interface IMultipartPackageStorageWriter
    {
        /// <summary>
        /// Prepares a container for holding the uploaded items based on the instruction set 
        /// </summary>
        /// <returns></returns>
        Task<Guid> CreatePackage(Stream data);
        
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