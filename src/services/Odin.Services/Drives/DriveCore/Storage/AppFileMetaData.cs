using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Storage
{
    /// <summary>
    /// Metadata provided by the app to describe the file
    /// </summary>
    public class AppFileMetaData : IAppFileMetaData
    {
        public Guid? UniqueId { get; set; }

        public List<Guid> Tags { get; set; }

        public int FileType { get; set; }

        public int DataType { get; set; }

        public Guid? GroupId { get; set; }

        public UnixTimeUtc? UserDate { get; set; }

        public string Content { get; set; }

        public ThumbnailContent PreviewThumbnail { get; set; }

        public int ArchivalStatus { get; set; }
    }
}