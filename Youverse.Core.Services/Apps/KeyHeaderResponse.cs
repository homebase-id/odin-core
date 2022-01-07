using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class KeyHeaderResponse
    {
        public byte[] Iv { get; set; }

        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
    }
}