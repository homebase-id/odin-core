using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Offers creation, read, write of data on drives.
    /// </summary>
    public interface IDriveService
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
        DriveFileId CreateFileId(Guid driveId);

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.  To write the KeyHeader, use 
        /// </summary>
        Task WritePartStream(DriveFileId file, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        Task<long> GetFileSize(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        /// <returns></returns>
        Task<Stream> GetFilePartStream(DriveFileId file, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Get the <see cref="StorageDisposition"/> for the specified  <param name="fileId"></param>
        /// </summary>
        /// <returns></returns>
        Task<StorageDisposition> GetStorageType(DriveFileId file);

        /// <summary>
        /// Returns the <see cref="EncryptedKeyHeader"/> for a given file.
        /// </summary>
        /// <returns></returns>
        Task<EncryptedKeyHeader> GetKeyHeader(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Deletes all parts matching <param name="file"></param>
        /// </summary>
        /// <returns></returns>
        Task Delete(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Moves the specified <param name="file"></param> to <see cref="StorageDisposition.LongTerm"/>
        /// </summary>
        /// <returns></returns>
        Task MoveToLongTerm(DriveFileId file);

        /// <summary>
        /// Moves the specified <param name="file"></param> to <see cref="StorageDisposition.Temporary"/>
        /// </summary>
        /// <returns></returns>
        Task MoveToTemp(DriveFileId file);

        Task WriteKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Returns the current index which should be used for a given drive.
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        StorageDriveIndex GetCurrentIndex(Guid driveId);
        
        Task RebuildAllIndices();

        /// <summary>
        /// Rebuilds the index for a given Drive
        /// </summary>
        /// <param name="driveId"></param>
        /// <returns></returns>
        Task RebuildIndex(Guid driveId);
    }
}