using System;

namespace Youverse.Core.Services.Drives
{
    /// <summary>
    /// Drive and file info which identifies a file to be used externally to the host. i.e. you can send this to the client
    /// </summary>
    public class GlobalTransitIdFileIdentifier
    {
        /// <summary>
        /// The drive to access
        /// </summary>
        public TargetDrive TargetDrive { get; set; }
        
        /// <summary>
        /// The global transit id to retrieve
        /// </summary>
        public Guid FileId { get; set; }
        
        public bool HasValue()
        {
            return FileId != Guid.NewGuid() && TargetDrive.IsValid();
        }

        public static bool operator ==(GlobalTransitIdFileIdentifier d1, GlobalTransitIdFileIdentifier d2)
        {
            if (ReferenceEquals(d1, d2))
            {
                return true;
            }

            return d1?.FileId == d2?.FileId && d1.TargetDrive == d2.TargetDrive;
        }

        public static bool operator !=(GlobalTransitIdFileIdentifier d1, GlobalTransitIdFileIdentifier d2)
        {
            return !(d1 == d2);
        }
        
        public bool Equals(GlobalTransitIdFileIdentifier other)
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
            return Equals((GlobalTransitIdFileIdentifier)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TargetDrive, FileId);
        }
    }
}