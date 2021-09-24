using DotYou.Kernel.Services.Admin.Authentication;
using System;

// We'll need something like this on the identity:
//     List<TokenClientAppplicationManager> tokenApplicationList;


namespace DotYou.Kernel.Cryptography
{
    public static class AppTokenManager
    {
        /// <summary>
        /// Get the Application Kek by means of the LoginKek master key
        /// </summary>
        /// <param name="token">The ApplicationTokenData</param>
        /// <param name="LoginKek">The master key LoginKek</param>
        /// <returns>The (aes) decrypted Application KeK</returns>
        public static byte[] MasterGetApplicationKek(AppTokenData token, byte[] LoginKek)
        {
            return AesCbc.DecryptBytesFromBytes_Aes(token.kekAesAdek, LoginKek, token.iv);
        }


        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="token">The ApplicationTokenData</param>
        /// <param name="LoginKek">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        public static byte[] MasterGetApplicationDek(AppTokenData token, byte[] LoginKeK)
        {
            var appKek = MasterGetApplicationKek(token, LoginKeK);
            var appDek = GetApplicationDek(token, appKek);
            YFByteArray.WipeByteArray(appKek);
            
            return appDek;
        }


        /// <summary>
        /// Return the application DeK by means of the Application KeK
        /// </summary>
        /// <param name="token">The ApplicationTokenData</param>
        /// <param name="ApplicationKek">The application KeK</param>
        /// <returns>The decrypted DeK</returns>
        public static byte[] GetApplicationDek(AppTokenData token, byte[] ApplicationKek)
        {
            return YFByteArray.EquiByteArrayXor(token.akekXoredAdek, ApplicationKek);
        }

        // On creating a new application, e.g. 'chat' this is done only once:
        //
        //    It creates the application ID which is to be the first client cookie
        //    It creates the application specific AKeK and stores it XOR'ed with the ID
        //    It encrypts the AKeK and one for the master login KeK. 
        //    It returns the second client cookie which is the halfAkek needed to get the Akek.
        //
        // Remember you can only add a new applicaiton with a confirmation process
        // which will be a web based dialogue. So we will always have access to the
        // Login KeK in this context.
        //

        /// <summary>
        /// Create a new application. This should only be done once for an application, e.g. chat.
        /// </summary>
        /// <param name="name">Friendly name, not used</param>
        /// <param name="LoginKeK">The master key which can later be used to retrieve the aDeK or aKeK</param>
        /// <returns>A new ApplicationTokenData object</returns>
        public static AppTokenData CreateApplication(string name, byte[] LoginKeK)
        {
            var AdeK     = YFByteArray.GetRndByteArray(16); // Create the ApplicationDataEncryptionKey (AdeK)
            var AkeK     = YFByteArray.GetRndByteArray(16); // Create the ApplicationKeyEncryptionKey (AkeK)

            var token = new AppTokenData
            {
                id = YFByteArray.GetRndByteArray(16),
                name = name,
            };

            token.akekXoredAdek = XorManagement.XorEncrypt(AdeK, AkeK);
            (token.iv, token.kekAesAdek) = AesCbc.EncryptBytesToBytes_Aes(AkeK, LoginKeK);

            YFByteArray.WipeByteArray(AdeK);
            YFByteArray.WipeByteArray(AkeK);

            return token;
        }
    }
}
