using System;

namespace Odin.Services.Drives
{
    /// <summary>
    /// Drive and file info which identifies a file to be used externally to the host. i.e. you can send this to the client
    /// </summary>
    public class ClientUniqueIdFileIdentifier
    {
        /// <summary>
        /// The drive to access
        /// </summary>
        public TargetDrive TargetDrive { get; set; }
        
        /// <summary>
        /// The unique id to retrieve
        /// </summary>
        public Guid ClientUniqueId { get; set; }
        
        public bool HasValue()
        {
            return ClientUniqueId != Guid.NewGuid() && TargetDrive.IsValid();
        }

        public static bool operator ==(ClientUniqueIdFileIdentifier d1, ClientUniqueIdFileIdentifier d2)
        {
            if (ReferenceEquals(d1, d2))
            {
                return true;
            }

            return d1?.ClientUniqueId == d2?.ClientUniqueId && d1.TargetDrive == d2.TargetDrive;
        }

        public static bool operator !=(ClientUniqueIdFileIdentifier d1, ClientUniqueIdFileIdentifier d2)
        {
            return !(d1 == d2);
        }
        
        public bool Equals(ClientUniqueIdFileIdentifier other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(TargetDrive, other.TargetDrive) && ClientUniqueId.Equals(other.ClientUniqueId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ClientUniqueIdFileIdentifier)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TargetDrive, ClientUniqueId);
        }
    }
}