using System;
using System.Collections.Generic;
using Odin.Core.Time;

namespace Odin.Core.Services.Drives.DriveCore.Storage
{
    public interface IAppFileMetaData
    {
        /// <summary>
        /// A uniqueId assigned by the client by which this file can be accessed
        /// </summary>
        Guid? UniqueId { get; set; }
        
        /// <summary>
        /// Tags for describing the file.  this is indexed and can be used to find files by one or more tags
        /// </summary>
        List<Guid> Tags { get; set; }

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
        UnixTimeUtc? UserDate { get; set; }

        /// <summary>
        /// This is not searchable but rather available to be returned
        /// when querying the index so you do not have to retrieve the whole payload
        /// </summary>
        string JsonContent { get; set; }

        /// <summary>
        /// An id that can be used by the client to group this file with others
        /// </summary>
        public Guid? GroupId { get; set; }
        
        /// <summary>
        /// A tiny thumbnail, blurry and small to be seen when previewing
        /// content (i.e. scrolling past an image in chat or list of blog posts during first page load)
        /// </summary>
        public ImageDataContent PreviewThumbnail { get; set; }

    }
}