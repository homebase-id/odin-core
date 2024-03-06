using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    public class UploadAppFileMetaData : IAppFileMetaData
    {
        public Guid? UniqueId { get; set; }
        public List<Guid> Tags { get; set; }

        public int FileType { get; set; }

        public int DataType { get; set; }
        
        public UnixTimeUtc? UserDate { get; set; }

        public Guid? GroupId { get; set; }

        public int ArchivalStatus { get; set; }

        public string Content { get; set; }
        
        public ThumbnailContent PreviewThumbnail { get; set; }
        
    }
}   