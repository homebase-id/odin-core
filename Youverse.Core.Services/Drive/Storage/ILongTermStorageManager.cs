using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Handles the storage, retrieval, and management of data storage for a single <see cref="StorageDrive"/>.
    /// </summary>
    public interface ILongTermStorageManager
    {
        /// <summary>
        /// The drive managed by this instance
        /// </summary>
        StorageDrive Drive { get; }
        
        /// <summary>
        /// Creates an Id for storing a file
        /// </summary>
        /// <returns></returns>
        Guid CreateFileId();

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        Task WritePartStream(Guid fileId, FilePart part, Stream stream);

        Task<long> GetPayloadFileSize(Guid fileId);
        
        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        /// <returns></returns>
        Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart);
        
        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(Guid fileId);

        /// <summary>
        /// Checks if the file exists.  Returns true if all parts exist, otherwise false
        /// </summary>
        bool FileExists(Guid fileId);
        
        /// <summary>
        /// Deletes all parts matching <param name="fileId"></param>
        /// </summary>
        /// <returns></returns>
        Task Delete(Guid fileId);

        /// <summary>
        /// Moves the specified <param name="filePath"></param> to long term storage.
        /// </summary>
        /// <returns></returns>
        Task MoveToLongTerm(Guid fileId, string filePath, FilePart part);

        /// <summary>
        /// Returns an enumeration of <see cref="FileMetadata"/>; ordered by the most recently modified
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<IEnumerable<ServerFileHeader>> GetServerFileHeaders(PageOptions pageOptions);

        Task<ServerFileHeader> GetServerFileHeader(Guid fileId);
    }
}