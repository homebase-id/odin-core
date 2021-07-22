using System;


namespace DotYou.Kernel.Cryptography
{
    // So in all likelihood, this is what'll happen (application example):
    //
    //     Client sends HTTPS request to server. 
    //     Server get's the cookie 'Token' 
    //     Server loads DB key from Token and gets the ClientApplicationToken, can calculate KeK
    //     Server loads the DB with key from ClientApplicationToken.id and gets the ApplicationToken
    //     Can now calculate application DeK from here using the KeK
    //
    // List<AppplicationTokenMgmt> applicationList;
    //


    public class ApplicationTokenData
    {
        public string name;          // E.g. 'login', 'chat', 'diary'. Maýbe use another ID / Key here. Not used.
        public Guid id;              // A guid id
        public byte[] _kekXoredDek;  // (DeK ^ KeK) i.e. the DeK encrypted with the KeK
        public byte[] _aekAesDek;    // aes(DeK, AeK) i.e. the DeK encrypted with the KeK, id is the IV (not usable for 'login', empty)
        public byte[] _iv;           // IV for AES
    }

    public static class AppplicationTokenManager
    {
        // On creating a new application, e.g. 'chat' this is done only once.
        // It creates the application specific AeK and encrypts it for the 
        // application specific KeK and one for the master login KeK. 
        // Remember you can only add a new applicaiton with a confirmation process
        // which will be a web based dialogue. So we will always have access to the
        // Login KeK in this context.

        // LoginDeK rather than KeK?
        public static ApplicationTokenData CreateApplication(string name, byte[] ApplicationKeK, byte[] LoginKeK)
        {
            var AeK = YFByteArray.GetRndByteArray(16); // Create the ApplicationEncryptionKey (AeK)

            var token = new ApplicationTokenData
            {
                _kekXoredDek = XorManagement.XorEncrypt(AeK, ApplicationKeK),
                name = name,
                id = new Guid()
            };

            (token._iv, token._aekAesDek) = AesCbc.EncryptBytesToBytes_Aes(AeK, LoginKeK);

            YFByteArray.WipeByteArray(AeK);

            return token;
        }

        // The application ½ KeK and server ½ Kek will join to form 
        // the application KeK that will unlock the DeK.
        public static byte[] GetApplicationAek(ApplicationTokenData token, byte[] applicationKeK)
        {
            return XorManagement.XorEncrypt(token._kekXoredDek, applicationKeK);
        }

        // The LoginKeK can be used to access the Application DeK
        // The LoginKeK is a master key across all applications.
        public static byte[] GetApplicationAekMaster(ApplicationTokenData token, byte[] LoginKeK)
        {
            return AesCbc.DecryptBytesFromBytes_Aes(token._aekAesDek, LoginKeK, token._iv);
        }
    }
}
