using System;

namespace Youverse.Core.Services.Transit
{
    public class KeyHeader
    {
        public Guid Id { get; set; }
        public string EncryptedKey64 { get; set; }
        
        //key1, key 2, RSA, Shared Secret
        public byte[] GetKeyBytes { get; set; }
    }
    
}