using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Resolves information about a container.
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Creates a new storage drive
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        Task<StorageDrive> CreateDrive(string name);

        Task<StorageDrive> GetDrive(Guid driveId, bool failIfInvalid = false);

        /// <summary>
        /// Returns a list of the containers in the system
        /// </summary>
        /// <returns></returns>
        Task<PagedResult<StorageDrive>> GetDrives(PageOptions pageOptions);

        /// <summary>
        /// Creates an Id for storing a file
        /// </summary>
        /// <returns></returns>
        Guid CreateFileId(Guid driveId);

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.  To write the KeyHeader, use 
        /// </summary>
        Task WritePartStream(Guid driveId, Guid fileId, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        Task<long> GetFileSize(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        /// <param name="filePart"></param>
        /// <returns></returns>
        Task<Stream> GetFilePartStream(Guid driveId, Guid fileId, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Get the <see cref="StorageDisposition"/> for the specified  <param name="fileId"></param>
        /// </summary>
        /// <returns></returns>
        Task<StorageDisposition> GetStorageType(Guid driveId, Guid fileId);

        /// <summary>
        /// Returns the <see cref="EncryptedKeyHeader"/> for a given file.
        /// </summary>
        /// <returns></returns>
        Task<EncryptedKeyHeader> GetKeyHeader(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Deletes all parts matching <param name="fileId"></param>
        /// </summary>
        /// <returns></returns>
        Task Delete(Guid driveId, Guid fileId, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Moves the specified <param name="fileId"></param> to <see cref="StorageDisposition.LongTerm"/>
        /// </summary>
        /// <returns></returns>
        Task MoveToLongTerm(Guid driveId, Guid fileId);

        /// <summary>
        /// Moves the specified <param name="fileId"></param> to <see cref="StorageDisposition.Temporary"/>
        /// </summary>
        /// <returns></returns>
        Task MoveToTemp(Guid driveId, Guid fileId);

        Task WriteKeyHeader(Guid driveId, Guid fileId, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
    }
}