using System;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadAppFileMetaData: IAppFileMetaData
    {
        public int FileType { get; set; }
        
        public Guid? PrimaryCategoryId { get; set; }
        
        public Guid? SecondaryCategoryId { get; set; }

        public bool ContentIsComplete { get; set; }
        
        public bool PayloadIsEncrypted { get; set; }
        
        public string DistinguishedName { get; set; }

        public string JsonContent { get; set; }
        
    }
}