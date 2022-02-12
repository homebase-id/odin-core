using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadAppFileMetaData: IAppFileMetaData
    {
        public List<Guid> Tags { get; set; }
        public int FileType { get; set; }
        
        public bool ContentIsComplete { get; set; }
        
        public bool PayloadIsEncrypted { get; set; }
        

        public string JsonContent { get; set; }
        
    }
}