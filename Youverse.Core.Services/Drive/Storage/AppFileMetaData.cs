using System;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Metadata provided by the app to describe the file
    /// </summary>
    public class AppFileMetaData : IAppFileMetaData
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