using System;

namespace Youverse.Core.Services.Transit
{
    public enum EncryptionType
    {
        Aes = 11,
        Rsa = 22
    }
    
    public class EncryptedKeyHeader
    {
        public EncryptionType Type { get; set; }

        /// <summary>
        /// The encrypted bytes of the data
        /// </summary>
        public byte[] Data { get; set; }
    }
    
    public class KeyHeader
    {

        public UInt32 Crc { get; set; }
        
        public byte[] Iv { get; set; }
        
        public byte[] AesKey { get; set; }
        
    }
    
}