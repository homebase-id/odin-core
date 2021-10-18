
namespace Youverse.Core.Cryptography.Data
{
    public class AppRegistrationData
    {
        public byte[] encryptedDek;  // The application KeK encrypted with the (login) KeK, i.e. aes(aDEK, KeK)
        public byte[] iv;            // IV for AES
    }
}