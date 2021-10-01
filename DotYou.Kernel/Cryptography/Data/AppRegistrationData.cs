
namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class AppRegistrationData
    {
        public byte[] encryptedDek;  // The application KeK encrypted with the (login) KeK, i.e. aes(aDEK, KeK)
        public byte[] iv;            // IV for AES
    }
}