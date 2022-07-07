using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadAppFileMetaData : IAppFileMetaData
    {

        /// <summary>
        /// Any number of tags describing the data
        /// </summary>
        public List<Guid> Tags { get; set; }

        /// <summary>
        /// A user specified value to indicate the type of file.  Avoid confusion with content-type.  This is specific to the application
        /// </summary>
        public int FileType { get; set; }

        /// <summary>
        /// A user specified value to indicate the type of data
        /// </summary>
        public int DataType { get; set; }
        
        /// <summary>
        /// A user specified date a date in Unix time UTC. (i.e. date a photo was taken)
        /// </summary>
        public ulong UserDate { get; set; }

        /// <summary>
        /// A flag indicating if the <see cref="JsonContent"/> contains the whole of the file and therefore you do not need to retrieve the payload.
        /// </summary>
        public bool ContentIsComplete { get; set; }

        /// <summary>
        /// A header describing the payload
        /// </summary>
        public string JsonContent { get; set; }

    }
}