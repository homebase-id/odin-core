using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Handles the storage, retrieval, and management of data storage for a single <see cref="StorageDrive"/>.
    /// </summary>
    public interface IStorageManager
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
        /// Writes a stream for a given file and part to the configured provider.  To write the KeyHeader, use 
        /// </summary>
        Task WritePartStream(Guid fileId, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

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

        Task WriteKeyHeader(Guid fileId, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
        
        Task RebuildIndex();

        Task LoadLatestIndex();

        /// <summary>
        /// Gets the current index which should be used to query this drive
        /// </summary>
        /// <returns></returns>
        StorageDriveIndex GetCurrentIndex();

    }
}