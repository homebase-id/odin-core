using System;
using Odin.Core;
using Odin.Services.Base;

namespace Odin.Services.Drives
{
    /// <summary>
    /// Drive and file info which identifies a file to be used externally to the host. i.e. you can send this to the client
    /// </summary>
    public class ExternalFileIdentifier
    {
        /// <summary>
        /// The drive to access
        /// </summary>
        public TargetDrive TargetDrive { get; set; }

        /// <summary>
        /// The fileId to retrieve
        /// </summary>
        public Guid FileId { get; set; }

        public byte[] ToKey()
        {
            return ByteArrayUtil.Combine(FileId.ToByteArray(), TargetDrive.ToKey());
        }

        public bool HasValue()
        {
            return FileId != Guid.Empty && TargetDrive.IsValid();
        }

        public static bool operator ==(ExternalFileIdentifier d1, ExternalFileIdentifier d2)
        {
            if (ReferenceEquals(d1, d2))
            {
                return true;
            }

            return d1?.FileId == d2?.FileId && d1.TargetDrive == d2.TargetDrive;
        }

        public static bool operator !=(ExternalFileIdentifier d1, ExternalFileIdentifier d2)
        {
            return !(d1 == d2);
        }

        public bool Equals(ExternalFileIdentifier other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(TargetDrive, other.TargetDrive) && FileId.Equals(other.FileId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ExternalFileIdentifier)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TargetDrive, FileId);
        }

        public override string ToString()
        {
            return $"File:[{this.FileId}]\tTargetDrive:[{this.TargetDrive}]";
        }

        public FileIdentifier ToFileIdentifier()
        {
            return new FileIdentifier()
            {
                FileId = this.FileId,
                TargetDrive = this.TargetDrive
            };
        }

        // public FileIdentifier ToV2FileIdentifier()
        // {
        //     return new FileIdentifier()
        //     {
        //         FileId = this.FileId,
        //         DriveId = this.TargetDrive.Alias
        //     };
        // }
    }
}