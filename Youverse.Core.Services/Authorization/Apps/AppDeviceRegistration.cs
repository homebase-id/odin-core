using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppDeviceRegistration
    {
        public Guid Id { get; set; }
        
        public Guid ApplicationId { get; set; }
        
        public byte[] UniqueDeviceId { get; set; }

        public SymmetricKeyEncryptedXor keyHalfKek { get; set; }
        // public byte[] HalfAdek { get; set; } // Random 16-byte client cookie needed to calculate the application DeK

        public byte[] SharedSecret { get; set; } // The secret shared with the client. We need one per client
        public bool IsRevoked { get; set; }
        
    }
}