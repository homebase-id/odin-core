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
        /// Writes a stream to the drive's temporary storage
        /// </summary>
        Task WriteTempStream(DriveFileId file, string extension, Stream stream);
        
        Task<Stream> GetTempStream(DriveFileId file, string extension);

        /// <summary>
        /// Stores the metadata and associated payload (from the temp storage) in long term storage 
        /// </summary>
        /// <returns></returns>
        Task StoreLongTerm(KeyHeader keyHeader, FileMetadata metadata, string payloadExtension);
        
        /// <summary>
        /// Deletes the specified temp file matching the driveId, fileId and extension
        /// </summary>
        /// <returns></returns>
        Task DeleteTempFile(DriveFileId file, string extension);

        /// <summary>
        /// Deletes all temp files matching the fileId regardless of extension
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task DeleteTempFiles(DriveFileId file);
        
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
        /// Returns true if all parts of the file exist, otherwise false.
        /// </summary>
        //// <returns></returns>
        bool FileExists(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Deletes all parts matching <param name="file"></param>
        /// </summary>
        /// <returns></returns>
        Task DeleteLongTermFile(DriveFileId file, StorageDisposition storageDisposition = StorageDisposition.LongTerm);


        Task WriteEncryptedKeyHeader(DriveFileId file, EncryptedKeyHeader encryptedKeyHeader, StorageDisposition storageDisposition = StorageDisposition.LongTerm);

        Task<IEnumerable<FileMetadata>> GetMetadataFiles(Guid driveId, PageOptions pageOptions);

        /// <summary>
        /// Encrypts and writes a KeyHeader
        /// </summary>
        Task<EncryptedKeyHeader> WriteKeyHeader(DriveFileId file, KeyHeader keyHeader);
    }
}