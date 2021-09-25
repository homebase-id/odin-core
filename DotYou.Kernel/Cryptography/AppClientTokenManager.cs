﻿using DotYou.Kernel.Services.Admin.Authentication;
using System;

//
// This is the mapping table between a client's cookies and the
// application tokens.
//
//    {token-id, application-id, half-akek, shared-secret}
//
// Here's the high level flow:
//
//     Client sends (HTTPS) request to server. 
//     Server get's the cookies 'Token' and 'Half'
//     Server looks up a ClientApplication entry by means of Token
//     Server calculates the Application Kek by means of Half
//     Server loads TokenApplicationData using the application-id from the table.
//     Server accesses the Application KeK by mean of Half
//     Server accesses the Application DeK by means of the Application KeK
//
// We'll need something like this on the identity:
//     List<TokenClientAppplicationManager> tokenClientApplicationList;
//

namespace DotYou.Kernel.Cryptography
{
    public static class AppClientTokenManager
    {
        // For each client that needs to connect to an application call this function
        //
        //    It creates the ClientApplication table entry.
        //    It creates the tokenId which is to be the client's cookie equivalent ID.
        //    It creates the cookie2 which is to be the client's secod cookie needed to decrypt the AKeK
        //    It encrypts the AKeK and one for the master login KeK. 
        //    It returns the second client cookie which is the halfAkek needed to get the Akek 
        //      and the table entry
        //

        public static (byte[] halfCookie, AppClientTokenData token) CreateClientToken(byte[] ApplicationId, byte[] ApplicationKeK, byte[] sharedSecret = null)
        {
            var token = new AppClientTokenData
            {
                deviceApplicationId  = new Guid(YFByteArray.GetRndByteArray(16)),
                applicationId = new Guid(ApplicationId)
            };

            var halfCookie = YFByteArray.GetRndByteArray(16);
            token.halfAdek = XorManagement.XorEncrypt(ApplicationKeK, halfCookie);

            if (sharedSecret == null)
                token.SharedSecret = YFByteArray.GetRndByteArray(16);
            else
            {
                if (sharedSecret.Length != 16)
                    throw new Exception("Shared secret expected 16 bytes");
                token.SharedSecret = sharedSecret;
            }

            return (halfCookie, token);
        }


        // The client cookie2 application ½ KeK and server's ½ application Kek will join to form 
        // the application KeK that will unlock the DeK.
        public static byte[] GetApplicationKek(AppClientTokenData clientToken, byte[] cookie2)
        {
            return XorManagement.XorEncrypt(clientToken.halfAdek, cookie2);
        }
    }
}
