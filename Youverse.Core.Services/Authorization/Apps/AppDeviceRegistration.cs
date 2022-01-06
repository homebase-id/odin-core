using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppDeviceRegistration
    {
        public Guid Id { get; set; }
        
        public Guid ApplicationId { get; set; }
        
        public byte[] UniqueDeviceId { get; set; }

        public SymmetricKeyEncryptedXor EncryptedAppKey { get; set; }
        
        public byte[] SharedSecretKey { get; set; }
        
        public bool IsRevoked { get; set; }
        
    }
}