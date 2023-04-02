using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drives.DriveCore.Storage
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

        public bool ContentIsComplete { get; set; }
        
        public string JsonContent { get; set; }
        
        public ImageDataContent PreviewThumbnail { get; set; }
        
        public IEnumerable<ImageDataHeader> AdditionalThumbnails { get; set; }
        
        public bool IsArchived { get; set; }
    }

}