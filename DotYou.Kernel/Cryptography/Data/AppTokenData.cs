
namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class AppTokenData
    {
        public string name;           // Not used. Could be e.g. 'login', 'chat', 'diary'. Maýbe use another ID / Key here.
        public byte[] id;             // The token ID (this is the value stored in the client cookie 'token' for the application)

        public byte[] akekXoredAdek; // The Application DeK encrypted with the Application KeK i.e. (ADeK ^ AKeK)
        public byte[] kekAesAkek;    // The application KeK encrypted with the (login) KeK, i.e. aes(aKeK, KeK)
                                     //   can be used by login priv. to access the Application DeK above.
        public byte[] iv;            // IV for AES
    }
}