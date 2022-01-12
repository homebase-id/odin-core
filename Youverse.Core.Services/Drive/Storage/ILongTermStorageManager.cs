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
        Task WritePartStream(Guid fileId, FilePart part, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        Task<long> GetFileSize(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
        
        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        /// <returns></returns>
        Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Get the <see cref="StorageDisposition"/> for the specified  <param name="fileId"></param>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task<StorageDisposition> GetStorageType(Guid fileId);

        /// <summary>
        /// Returns the <see cref="EncryptedKeyHeader"/> for a given file.
        /// </summary>
        /// <returns></returns>
        Task<EncryptedKeyHeader> GetKeyHeader(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Checks if the file exists.  Returns true if all parts exist, otherwise false
        /// </summary>
        bool FileExists(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
        
        /// <summary>
        /// Deletes all parts matching <param name="fileId"></param>
        /// </summary>
        /// <returns></returns>
        Task Delete(Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Moves the specified <param name="fileId"></param> to <see cref="StorageDisposition.LongTerm"/>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task MoveToLongTerm(Guid fileId);

        /// <summary>
        /// Moves the specified <param name="fileId"></param> to <see cref="StorageDisposition.Temporary"/>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task MoveToTemp(Guid fileId);

        /// <summary>
        /// Writes an <see cref="EncryptedKeyHeader"/> to storage
        /// </summary>
        /// <param name="fileId"></param>
        /// <param name="encryptedKeyHeader"></param>
        /// <param name="storageDisposition"></param>
        /// <returns></returns>
        Task WriteEncryptedKeyHeader(Guid fileId, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
        
        /// <summary>
        /// Returns an enumeration of <see cref="FileMetadata"/>; ordered by the most recently modified
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<IEnumerable<FileMetadata>> GetMetadataFiles(PageOptions pageOptions);

        Task<FileMetadata> GetMetadata(Guid fileId, StorageDisposition storageDisposition);
    }
}