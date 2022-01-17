using System;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// A specifier for a file being written to a drive
    /// </summary>
    public struct DriveFileId
    {
        /// <summary>
        /// The drive to which this file is written
        /// </summary>
        public Guid DriveId { get; set; }

        /// <summary>
        /// The fileId
        /// </summary>
        public Guid FileId { get; set; }

        public bool IsValid()
        {
            return DriveId != Guid.Empty && FileId != Guid.Empty;
        }
        
        public static bool operator ==(DriveFileId d1, DriveFileId d2)
        {
            return d1.DriveId == d2.DriveId && d1.FileId == d2.FileId;
        }

        public static bool operator !=(DriveFileId d1, DriveFileId d2) => !(d1 == d2);

        public override bool Equals(object? obj)
        {
            var d2 = (DriveFileId) obj;
            return this == d2;
        }

        public override int GetHashCode()
        {
            return this.DriveId.GetHashCode() + this.FileId.GetHashCode();
        }

        public static DriveFileId Redacted()
        {
            //HACK
            var g = Guid.Parse("11111111-1111-1111-1111-111111111111");

            return new DriveFileId()
            {
                DriveId = g,
                FileId = g
            };
        }
    }
}