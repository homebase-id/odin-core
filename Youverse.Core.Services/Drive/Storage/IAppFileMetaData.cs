using System;

namespace Youverse.Core.Services.Drive.Storage
{
    public interface IAppFileMetaData
    {
        /// <summary>
        /// A file type specific to an app.  This is indexed and be used to query data
        /// </summary>
        int FileType { get; set; }

        /// <summary>
        /// A primary categoryId specific to an app.  This is indexed and can be used to query data.
        /// </summary>
        Guid? PrimaryCategoryId { get; set; }

        /// <summary>
        /// A secondary categoryId specific to an app  It only makes sense for this to be set when the primary category is set, yet there is no enforcement.  This is indexed and can be used to query data.
        /// </summary>
        Guid? SecondaryCategoryId { get; set; }
        
        /// <summary>
        /// If true, the <see cref="JsonContent"/> is the full payload of information, otherwise, it is partial (like a preview of a chat message)
        /// </summary>
        bool ContentIsComplete { get; set; }
        
        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        bool PayloadIsEncrypted { get; set; }
        
        /// <summary>
        /// A standard distinguished name used to classify the file.  
        /// </summary>
        public string DistinguishedName { get; set; }

        /// <summary>
        /// The JsonPayload to be included in the index.  This is not searchable but rather available to be returned
        /// when querying the index so you do not have to retrieve the whole payload
        /// </summary>
        string JsonContent { get; set; }
    }
}