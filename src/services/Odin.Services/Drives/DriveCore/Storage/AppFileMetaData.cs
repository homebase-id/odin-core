using System;
using System.Collections.Generic;
using Odin.Core.Exceptions;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Metadata provided by the app to describe the file
    /// </summary>
    public class AppFileMetaData : IAppFileMetaData
    {
        public static readonly int MaxTagCount = 50;
        public static readonly int MaxAppDataContentLength = 10 * 1024;

        public Guid? UniqueId { get; set; }

        public List<Guid> Tags { get; set; }

        public int FileType { get; set; }

        public int DataType { get; set; }

        public Guid? GroupId { get; set; }

        public UnixTimeUtc? UserDate { get; set; }

        public string Content { get; set; }

        public ThumbnailContent PreviewThumbnail { get; set; }

        public int ArchivalStatus { get; set; }


        public bool TryValidate()
        {
            try
            {
                Validate();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Validate()
        {
            if ((Tags?.Count ?? 0) > MaxTagCount)
                throw new OdinClientException($"Too many Tags count {Tags?.Count ?? 0} in AppFileMetaData max {MaxTagCount}");

            if ((Content?.Length ?? 0) > MaxAppDataContentLength)
                throw new OdinClientException($"Content length {Content?.Length ?? 0} in AppFileMetaData max {MaxAppDataContentLength}",
                    OdinClientErrorCode.MaxContentLengthExceeded);

            PreviewThumbnail?.Validate();
        }
    }
}