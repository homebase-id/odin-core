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
        InternalDriveFileId CreateFileId(Guid driveId);

        Task WriteMetaData(InternalDriveFileId file, FileMetadata metadata);

        /// <summary>
        /// Writes the payload stream
        /// </summary>
        /// <param name="file"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        Task WritePayload(InternalDriveFileId file, Stream stream);

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider. 
        /// </summary>
        Task WritePartStream(InternalDriveFileId file, FilePart filePart, Stream stream);

        /// <summary>
        /// Writes a stream to the drive's temporary storage
        /// </summary>
        Task WriteTempStream(InternalDriveFileId file, string extension, Stream stream);

        Task<Stream> GetTempStream(InternalDriveFileId file, string extension);

        /// <summary>
        /// Deserializes the file to an instance of {T}.  Assumes format is JSON
        /// </summary>
        /// <returns></returns>
        Task<T> GetDeserializedStream<T>(InternalDriveFileId file, string extension, StorageDisposition disposition = StorageDisposition.LongTerm);

        /// <summary>
        /// Stores the metadata and associated payload (from the temp storage) in long term storage 
        /// </summary>
        /// <returns></returns>
        Task StoreLongTerm(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata metadata, string payloadExtension);

        /// <summary>
        /// Deletes the specified temp file matching the driveId, fileId and extension
        /// </summary>
        /// <returns></returns>
        Task DeleteTempFile(InternalDriveFileId file, string extension);

        /// <summary>
        /// Deletes all temp files matching the fileId regardless of extension
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task DeleteTempFiles(InternalDriveFileId file);

        /// <summary>
        /// Gets the <see cref="FileMetadata"/>
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        Task<FileMetadata> GetMetadata(InternalDriveFileId file);


        Task<Stream> GetPayloadStream(InternalDriveFileId file);

        Task<long> GetPayloadSize(InternalDriveFileId file);
        
        /// <summary>
        /// Returns the <see cref="EncryptedKeyHeader"/> for a given file.
        /// </summary>
        /// <returns></returns>
        Task<EncryptedKeyHeader> GetEncryptedKeyHeader(InternalDriveFileId file);

        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(InternalDriveFileId file);

        /// <summary>
        /// Returns true if all parts of the file exist, otherwise false.
        /// </summary>
        //// <returns></returns>
        bool FileExists(InternalDriveFileId file);

        /// <summary>
        /// Deletes all parts matching <param name="file"></param>
        /// </summary>
        /// <returns></returns>
        Task DeleteLongTermFile(InternalDriveFileId file);

        Task WriteEncryptedKeyHeader(InternalDriveFileId file, EncryptedKeyHeader encryptedKeyHeader);

        Task<IEnumerable<FileMetadata>> GetMetadataFiles(Guid driveId, PageOptions pageOptions);

        /// <summary>
        /// Encrypts and writes a KeyHeader
        /// </summary>
        Task<EncryptedKeyHeader> WriteKeyHeader(InternalDriveFileId file, KeyHeader keyHeader);

        /// <summary>
        /// Returns the payload bytes.  If the payload is larger than the max size we will
        /// load into memory, the tooLarge result will be true.
        /// </summary>
        /// <returns></returns>
        Task<(bool tooLarge, long size, byte[] bytes)> GetPayloadBytes(InternalDriveFileId file);
    }
}