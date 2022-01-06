using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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

        event EventHandler<DriveFileChangedArgs> FileChanged;

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

        Task WriteMetaData(DriveFileId file, FileMetadata metadata, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Writes the payload stream
        /// </summary>
        /// <param name="file"></param>
        /// <param name="stream"></param>
        /// <param name="storageDisposition"></param>
        /// <returns></returns>
        Task WritePayload(DriveFileId file, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider. 
        /// </summary>
        Task WritePartStream(DriveFileId file, FilePart filePart, Stream stream, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Gets the <see cref="FileMetadata"/>
        /// </summary>
        /// <param name="file"></param>
        /// <param name="storageDisposition"></param>
        /// <returns></returns>
        Task<FileMetadata> GetMetadata(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        Task<Stream> GetPayloadStream(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
        
        Task<long> GetFileSize(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        /// <returns></returns>
        Task<Stream> GetFilePartStream(DriveFileId file, FilePart filePart, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Get the <see cref="StorageDisposition"/> for the specified
        /// </summary>
        /// <returns></returns>
        Task<StorageDisposition> GetStorageType(DriveFileId file);

        /// <summary>
        /// Returns the <see cref="EncryptedKeyHeader"/> for a given file.
        /// </summary>
        /// <returns></returns>
        Task<EncryptedKeyHeader> GetEncryptedKeyHeader(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

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

        Task WriteEncryptedKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm);
        
        Task<IEnumerable<FileMetadata>> GetMetadataFiles(Guid driveId, PageOptions pageOptions);
        
        /// <summary>
        /// Converts a transfer key header to a long term key header and stores it for the specified file.
        /// </summary>
        Task<EncryptedKeyHeader> WriteTransferKeyHeader(DriveFileId file, EncryptedKeyHeader transferEncryptedKeyHeader, StorageDisposition storageDisposition);
        
    }
}