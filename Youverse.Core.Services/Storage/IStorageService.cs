using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Services.Transit;

namespace Youverse.Core.Services.Storage
{
    /// <summary>
    /// Handles the storage, retrieval, and management of <see cref="EncryptedFile"/>s
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Creates an Id for storing a file
        /// </summary>
        /// <returns></returns>
        Guid CreateId();
        
        /// <summary>
        /// Writes a stream for a given file and part to the configured provider.  To write the KeyHeader, use 
        /// </summary>
        Task WritePartStream(Guid id, FilePart filePart, Stream stream, StorageType storageType = StorageType.LongTerm);

        Task<long> GetFileSize(Guid id, StorageType storageType = StorageType.LongTerm);
        
        Task<Guid> SaveMedia(MediaData mediaData, bool giveNewId = false);

        Task<Guid> SaveMedia(MediaMetaData metaData, Stream stream, bool giveNewId = false, StorageType storageType = StorageType.LongTerm);

        Task<MediaData> GetMedia(Guid id);

        Task<MediaMetaData> GetMetaData(Guid id, StorageType storageType = StorageType.LongTerm);

        Task<Stream> GetMediaStream(Guid id, StorageType storageType = StorageType.LongTerm);
        
        /// <summary>
        /// Gets a read stream for the given <see cref="FilePart"/>
        /// </summary>
        /// <param name="fileId"></param>
        /// <param name="filePart"></param>
        /// <returns></returns>
        Task<Stream> GetFilePartStream(Guid fileId, FilePart filePart, StorageType storageType = StorageType.LongTerm);
        
        /// <summary>
        /// Get the <see cref="StorageType"/> for the specified  <param name="fileId"></param>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task<StorageType> GetStorageType(Guid fileId);
        

        /// <summary>
        /// Returns the <see cref="EncryptedKeyHeader"/> for a given file.
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task<EncryptedKeyHeader> GetKeyHeader(Guid fileId, StorageType storageType = StorageType.LongTerm);
        
        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <param name="fileId"></param>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(Guid fileId, StorageType storageType = StorageType.LongTerm);

        /// <summary>
        /// Deletes all parts matching <param name="fileId"></param>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task Delete(Guid fileId, StorageType storageType = StorageType.LongTerm);

        /// <summary>
        /// Moves the specified <param name="fileId"></param> to <see cref="StorageType.LongTerm"/>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task MoveToLongTerm(Guid fileId);

        /// <summary>
        /// Moves the specified <param name="fileId"></param> to <see cref="StorageType.Temporary"/>
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        Task MoveToTemp(Guid fileId);
    }
}