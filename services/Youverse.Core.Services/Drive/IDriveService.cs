using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Offers creation, read, write of data on drives.
    /// </summary>
    public interface IDriveService
    {
        /// <summary>
        /// Creates an Id for storing a file
        /// </summary>
        /// <returns></returns>
        InternalDriveFileId CreateInternalFileId(Guid driveId);

        Task UpdateActiveFileHeader(InternalDriveFileId file, ServerFileHeader header);

        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.
        /// </summary>
        Task WritePartStream(InternalDriveFileId file, FilePart filePart, Stream stream);

        /// <summary>
        /// Writes a stream to the drive's temporary storage
        /// </summary>
        Task<uint> WriteTempStream(InternalDriveFileId file, string extension, Stream stream);

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
        Task CommitNewFile(InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata, string payloadExtension);

        /// <summary>
        /// Overwrites the <param name="targetFile"/> with the <param name="tempFile"/>
        /// </summary>
        /// <returns></returns>
        Task OverwriteFile(InternalDriveFileId tempFile, InternalDriveFileId targetFile, KeyHeader keyHeader, FileMetadata metadata, ServerMetadata serverMetadata, string payloadExtension);

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
        /// Gets the <see cref="FileMetadata"/>.  Removes the access control list if anyone other than the owner is retrieving this file
        /// </summary>
        /// <param name="file"></param>
        /// <returns>The <see cref="FileMetadata"/> for the specified file and the <see cref="AccessControlList"/> of that specified file</returns>
        Task<ServerFileHeader> GetServerFileHeader(InternalDriveFileId file);

        public Task<Stream> GetPayloadStream(InternalDriveFileId file);

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
        Task HardDeleteLongTermFile(InternalDriveFileId file);

        /// <summary>
        /// Deletes all parts matching <param name="file"></param>
        /// </summary>
        /// <returns></returns>
        Task SoftDeleteLongTermFile(InternalDriveFileId file);

        Task<IEnumerable<ServerFileHeader>> GetMetadataFiles(Guid driveId, PageOptions pageOptions);

        Task<Stream> GetThumbnailPayloadStream(InternalDriveFileId file, int width, int height);

        Task WriteThumbnailStream(InternalDriveFileId file, int width, int height, Stream stream);

        string GetThumbnailFileExtension(int width, int height);

        /// <summary>
        /// Creates a new server file header with an encrypted key header for the specified file
        /// TODO: I'm not certain this is the right place for this method
        /// </summary>
        Task<ServerFileHeader> CreateServerFileHeader(InternalDriveFileId file, KeyHeader keyHeader, FileMetadata fileMetadata, ServerMetadata serverMetadata);
    }
}
