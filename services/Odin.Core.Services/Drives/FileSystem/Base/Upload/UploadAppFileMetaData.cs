using System;
using System.Collections.Generic;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload
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

        public bool ContentIsComplete { get; set; }

        public string JsonContent { get; set; }
        
        public ImageDataContent PreviewThumbnail { get; set; }
        
    }
}   