﻿using System;
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
        /// Writes a stream for a given file and part to the configured provider
        /// </summary>
        Task WritePartStream(Guid id, FilePart filePart, Stream stream);

        Task<long> GetFileSize(Guid id);
        
        Task<Guid> SaveMedia(MediaData mediaData, bool giveNewId = false);

        Task<Guid> SaveMedia(MediaMetaData metaData, Stream stream, bool giveNewId = false);

        Task<MediaData> GetMedia(Guid id);

        Task<MediaMetaData> GetMetaData(Guid id);

        Task<Stream> GetMediaStream(Guid id);
        Task<Stream> GetFilePartStream(Guid fileId);
        
        /// <summary>
        /// Ensures there is a valid file available for the given Id.
        /// </summary>
        /// <param name="fileId"></param>
        /// <exception cref="InvalidDataException">Throw if the file for the given Id is invalid or does not exist</exception>
        void AssertFileIsValid(Guid fileId);
    }
}