using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadAppFileMetaData : IAppFileMetaData
    {
        public List<byte[]> Tags { get; set; }

        public int FileType { get; set; }

        public int DataType { get; set; }
        
        public ulong UserDate { get; set; }

        public byte[] ThreadId { get; set; }

        public bool ContentIsComplete { get; set; }

        public string JsonContent { get; set; }
        
        public ThumbnailContent PreviewThumbnail { get; set; }
        
        public IEnumerable<ThumbnailHeader> AdditionalThumbnails { get; set; }

    }
}   