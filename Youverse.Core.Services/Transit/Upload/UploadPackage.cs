using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// A package/parcel to be send to a set of recipients
    /// </summary>
    public class UploadPackage
    {
        public UploadPackage(DriveFileId fileId, int expectedPartsCount)
        {
            this.File = fileId;
            ExpectedPartsCount = expectedPartsCount;
        }

        public RecipientList RecipientList { get; set; }

        public DriveFileId File { get; set; }
        public int ExpectedPartsCount { get; }
    }
}