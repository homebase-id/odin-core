using System;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppClientRegistration
    {
        public Guid Id { get; set; }
        
        public Guid ApplicationId { get; set; }
        
        public SymmetricKeyEncryptedXor ServerHalfAppKey { get; set; }
        
        public byte[] SharedSecretKey { get; set; }
        
        public bool IsRevoked { get; set; }
        
    }
}