
namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class AppKeyData
    {
        public string name;           // Not used. Could be e.g. 'login', 'chat', 'diary'. Maýbe use another ID / Key here.
        public byte[] id;             // The token ID (this is the value stored in the client cookie 'token' for the application)

        public byte[] encryptedDek;  // The application KeK encrypted with the (login) KeK, i.e. aes(aKeK, KeK)
        public byte[] iv;            // IV for AES
    }
}