using System;
using System.Collections.Generic;

namespace Youverse.Core.Services.Drive.Storage
{
    public interface IAppFileMetaData
    {
        /// <summary>
        /// Tags for describing the file.  this is indexed and can be used to find files by one or more tags
        /// </summary>
        List<byte[]> Tags { get; set; }

        /// <summary>
        /// A file type specific to an app.  This is indexed and be used to query data
        /// </summary>
        int FileType { get; set; }

        /// <summary>
        /// A data type specific to an app.  This is indexed and be used to query data
        /// </summary>
        int DataType { get; set; }

        /// <summary>
        /// A date specified in UnixTime for the file such as date photo captured, etc.
        /// </summary>
        ulong? UserDate { get; set; }

        /// <summary>
        /// If true, the <see cref="JsonContent"/> is the full payload of information, otherwise, it is partial (like a preview of a chat message)
        /// </summary>
        bool ContentIsComplete { get; set; }

        /// <summary>
        /// This is not searchable but rather available to be returned
        /// when querying the index so you do not have to retrieve the whole payload
        /// </summary>
        string JsonContent { get; set; }

        /// <summary>
        /// A tiny thumbnail, blurry and small to be seen when previewing
        /// content (i.e. scrolling past an image in chat or list of blog posts during first page load)
        /// </summary>
        public ThumbnailContent PreviewThumbnail { get; set; }

        /// <summary>
        /// Set of thumbnails for this file in addition to the <see cref="PreviewThumbnail"/>
        /// </summary>
        public IEnumerable<ThumbnailHeader> AdditionalThumbnails { get; set; }
    }
}