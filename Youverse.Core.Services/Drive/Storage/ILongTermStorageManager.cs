using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

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
        /// Gets a read stream of the thumbnail
        /// </summary>
        /// <param name="fileId"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        Task<Stream> GetThumbnail(Guid fileId, int width, int height);
        
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
        /// Removes all traces of a file and deletes its record from the index
        /// </summary>
        /// <returns></returns>
        Task HardDelete(Guid fileId);

        /// <summary>
        /// Removes the contents of the meta file while permanently deletes the payload and thumbnails.  Retains some fields of the metafile and updates the index accordingly
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task SoftDelete(Guid fileId);

        /// <summary>
        /// Moves the specified <param name="sourcePath"></param> to long term storage.
        /// </summary>
        /// <returns></returns>
        Task MoveToLongTerm(Guid fileId, string sourcePath, FilePart part);

        /// <summary>
        /// Returns an enumeration of <see cref="FileMetadata"/>; ordered by the most recently modified
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<IEnumerable<ServerFileHeader>> GetServerFileHeaders(PageOptions pageOptions);

        Task<ServerFileHeader> GetServerFileHeader(Guid fileId);
        
        Task WriteThumbnail(Guid fileId, int width, int height, Stream stream);

        Task MoveThumbnailToLongTerm(Guid fileId, string sourceThumbnail, int width, int height);
    }
}