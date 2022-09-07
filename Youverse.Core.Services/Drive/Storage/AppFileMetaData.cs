using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Metadata provided by the app to describe the file
    /// </summary>
    public class AppFileMetaData : IAppFileMetaData
    {
        public List<byte[]> Tags { get; set; }
        
        public int FileType { get; set; }
        
        public int DataType { get; set; }
        
        public byte[] GroupId { get; set; }
        
        public ulong UserDate { get; set; }

        public bool ContentIsComplete { get; set; }
        
        public string JsonContent { get; set; }
        
        public ThumbnailContent PreviewThumbnail { get; set; }
        
        public IEnumerable<ThumbnailHeader> AdditionalThumbnails { get; set; }
    }

}