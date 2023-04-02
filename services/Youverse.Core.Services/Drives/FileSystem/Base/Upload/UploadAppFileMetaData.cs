using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload
{
    public class UploadAppFileMetaData : IAppFileMetaData
    {
        public Guid? UniqueId { get; set; }
        public List<Guid> Tags { get; set; }

        public int FileType { get; set; }

        public int DataType { get; set; }
        
        public UnixTimeUtc? UserDate { get; set; }

        public Guid? GroupId { get; set; }

        public bool IsArchived { get; set; }

        public bool ContentIsComplete { get; set; }

        public string JsonContent { get; set; }
        
        public ImageDataContent PreviewThumbnail { get; set; }
        
        public IEnumerable<ImageDataHeader> AdditionalThumbnails { get; set; }
    }
}   