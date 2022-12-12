using System.IO;

namespace Youverse.Core.Cryptography
{
    public static class StreamExtensions
    {
        public static byte[] ToByteArray(this Stream stream)
        {
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                return memoryStream.ToArray();
            }
        }
    }
}