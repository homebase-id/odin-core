
namespace Youverse.Core.Cryptography.Data
{
    public class AppEncryptionKey
    {
        public byte[] EncryptedAppDeK;  // The application DEK encrypted with the (login) KeK, i.e. aes(AppDEK, KeK)
        public byte[] AppIV;            // IV for AES
    }
}