using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Storage
{
    /// <summary>
    /// Metadata provided by the app to describe the file
    /// </summary>
    public class AppFileMetaData : IAppFileMetaData
    {
        public List<Guid> Tags { get; set; }
        public int FileType { get; set; }
        
        public bool ContentIsComplete { get; set; }
        
        public bool PayloadIsEncrypted { get; set; }

        public string JsonContent { get; set; }
        
        public Guid Alias { get; set; }
    }
}