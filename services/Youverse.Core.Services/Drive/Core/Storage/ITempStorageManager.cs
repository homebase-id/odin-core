using System;
using System.IO;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive.Core.Storage
{
    /// <summary>
    /// Temporary storage for a given driven.  Used to stage incoming file parts from uploads and transfers.
    /// </summary>
    public interface ITempStorageManager
    {
        /// <summary>
        /// The drive managed by this instance
        /// </summary>
        StorageDrive Drive { get; }

        /// <summary>
        /// Gets a stream of data for the specified file
        /// </summary>
        Task<Stream> GetStream(Guid fileId, string extension);

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        Task<uint> WriteStream(Guid fileId, string extension, Stream stream);

        /// <summary>
        /// Checks if the file exists.  Returns true if all parts exist, otherwise false
        /// </summary>
        bool FileExists(Guid fileId, string extension);

        /// <summary>
        /// Deletes the file matching <param name="fileId"></param> and extension.
        /// </summary>
        /// <returns></returns>
        Task Delete(Guid fileId, string extension);

        /// <summary>
        /// Deletes all files matching <param name="fileId"></param> regardless of extension
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task Delete(Guid fileId);

        /// <summary>
        /// Gets the physical path of the specified file
        /// </summary>
        Task<string> GetPath(Guid fileId, string extension);
    }
}