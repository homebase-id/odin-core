namespace Youverse.Core.Services.Transit.Upload
{
    public static class MultipartSectionNames
    {
        public static string Recipients = "recipients"; //data is a byte array of encrypted data; the encrypted data is json after being decrypted
        public static string TransferEncryptedKeyHeader = "tekh";  //data is a json string
    }
}