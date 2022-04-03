using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Storage
{
    public interface IAppFileMetaData
    {
        /// <summary>
        /// Tags for describing the file.  this is indexed and can be used to find files by one or more tags
        /// </summary>
        List<Guid> Tags { get; set; }

        /// <summary>
        /// A file type specific to an app.  This is indexed and be used to query data
        /// </summary>
        int FileType { get; set; }

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
        /// The JsonPayload to be included in the index.  This is not searchable but rather available to be returned
        /// when querying the index so you do not have to retrieve the whole payload
        /// </summary>
        string JsonContent { get; set; }

        /// <summary>
        /// An alternative identifier for this file.  This can be used when you need a fixed handle in your client app to find this file.
        /// </summary>
        Guid Alias { get; set; }
    }
}