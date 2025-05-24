#nullable enable

using System;


namespace Odin.Services.Drives
{
    /// <summary>
    /// A specifier for a file being written to a drive
    /// </summary>
    public struct InternalDriveFileId
    {
        /// <summary>
        /// The drive to which this file is written
        /// </summary>
        public Guid DriveId { get; set; }

        /// <summary>
        /// The fileId
        /// </summary>
        public Guid FileId { get; set; }

        public InternalDriveFileId(StorageDrive drive, Guid fileId)
        {
            DriveId = drive.Id;
            FileId = fileId;
        }

        public InternalDriveFileId(Guid driveId, Guid fileId)
        {
            DriveId = driveId;
            FileId = fileId;
        }

        public bool IsValid()
        {
            return DriveId != Guid.Empty && FileId != Guid.Empty;
        }
        
        public TempFile AsTempFileUpload()
        {
            return new TempFile()
            {
                File = this,
                StorageType = TempStorageType.Upload
            };
        }
        
        public static bool operator ==(InternalDriveFileId d1, InternalDriveFileId d2)
        {
            return d1.DriveId == d2.DriveId && d1.FileId == d2.FileId;
        }

        public static bool operator !=(InternalDriveFileId d1, InternalDriveFileId d2) => !(d1 == d2);

        public override bool Equals(object? obj)
        {
            if (obj == null)
            {
                return false;
            }
            var d2 = (InternalDriveFileId) obj;
            return this == d2;
        }

        public override string ToString()
        {
            return $"FileId={this.FileId.ToString()} Drive={this.DriveId.ToString()}";
        }

        public override int GetHashCode()
        {
            return this.DriveId.GetHashCode() + this.FileId.GetHashCode();
        }

        public static InternalDriveFileId Redacted()
        {
            //HACK
            var g = Guid.Parse("11111111-1111-1111-1111-111111111111");

            return new InternalDriveFileId()
            {
                DriveId = g,
                FileId = g
            };
        }
    }
}