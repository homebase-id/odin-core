using System;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;

//
// After lots of thinking I strongly discourage expiring the login cookie(s).
// The only weak spot in our system is the password dialog, because the users
// tend to not look at the URL to make sure it's their own site. EvilGnix2 is
// the perfect example of what we need to protect against. 
//
// Therefore this will be much more effective IMO:
//   If we want to validate the password with e.g. a one month interval, then
//   once the month is up, we still log them in, we show their page with the
//   full name. And we show some 'secret' pictures that are only visible when
//   you're logged in. Something they're used to seeing when they're logged in.
//   In this context, we can ask them to re-validate their password. 
//   
// Thus, effectively, we make the 'new device' login screen only appear when it
// is truly a new device. It can have lots of warnings and arrows pointing up
// to the URL. Thus lowering the risk that the user will just blindly 
// enter their password because they get the same annoying dialog once+ per month.
//

namespace Youverse.Core.Cryptography
{

    public static class LoginTokenManager
    {
        /// <summary>
        /// For each (logged in) client that needs access call this function
        /// 
        ///    It creates a LoginTokenData object and returns the cookie 
        ///    and loginTokenData objects. 
        ///    The cookies to be stored on the client (HTTP ONLY, secure medium) are:
        ///       token.id (the index key)
        ///       halfCookie (half the loginKek)
        ///    The LoginTokenData object is to be stored on the server and retrievable 
        ///    via the index cookie as DB load key.
        /// </summary>
        /// <param name="LoginKeK"></param>
        /// <param name="sharedSecret"></param>
        /// <returns></returns>
        
        //public static (byte[] halfCookie, LoginTokenData token) CreateLoginToken(byte[] LoginKeK, byte[] sharedSecret)
        public static (byte[] halfCookie, LoginTokenData token) CreateLoginToken(NonceData loadedNoncePackage, IPasswordReply reply, RsaKeyListData listRsa)
        {
            var (hpwd64, kek64, sharedsecret64) = LoginKeyManager.ParsePasswordRSAReply(reply, listRsa);

            const int ttlSeconds = 31 * 24 * 3600; // Tokens can be semi-permanent.

            var token = new LoginTokenData
            {
                Id = ByteArrayUtil.GetRandomCryptoGuid(),
                SharedSecret = Convert.FromBase64String(sharedsecret64),
                ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds
            };

            var kek = new SecureKey(Convert.FromBase64String(kek64));
            token.ServerHalfOwnerConsoleKey = new SymmetricKeyEncryptedXor(kek, out var halfCookie);
            kek.Wipe();

            return (halfCookie, token);
        }


        // The client cookie2 application ½ KeK and server's ½ application Kek will join to form 
        // the application KeK that will unlock the DeK.
        public static SecureKey GetLoginKek(byte[] halfServer, byte[] halfClient)
        {
            return new SecureKey(XorManagement.XorEncrypt(halfServer, halfClient));
        }

        // The client cookie2 application ½ KeK and server's ½ application Kek will join to form 
        // the application KeK that will unlock the DeK.
        public static SecureKey GetLoginKek(LoginTokenData loginToken, byte[] halfCookie)
        {
            return loginToken.ServerHalfOwnerConsoleKey.DecryptKey(halfCookie);
            // return GetLoginKek(loginToken.HalfKey, halfCookie);
        }

        // XXX TODO Shouldn't there be a GetLoginDek here?
        // Or at least make a comment to where it is
        // And make the GetLoginKek and GetLoginDek use the KeyMaster class
    }
}
