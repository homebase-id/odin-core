using System;

namespace Odin.Services.Authorization.Apps
{
    /// <summary>
    /// Data sent to client when registering the user of an app on a given device (phone, browser, etc.)
    /// </summary>
    public class AppClientRegistrationResponse
    {
        /// <summary>
        /// The version of the encryption used to RSA encrypt <see cref="Data"/>.
        /// </summary>
        public int EncryptionVersion { get; set; }
        
        /// <summary>
        /// Used to lookup the server half of the app's Dek
        /// </summary>
        public Guid Token { get; set; }
        
        /// <summary>
        /// RSA encrypted response.  When encryption version == 1, the  first 16 bytes is token id, second 16 bytes is AccessTokenHalfKey, and last 16 bytes is SharedSecret
        /// </summary>
        public byte[] Data { get; set; }
    }
    
    public class AppClientEccRegistrationResponse
    {
        public int EncryptionVersion { get; set; }
        
        /// <summary>
        ///  The public key given by the server 
        /// </summary>
        public string ExchangePublicKeyJwkBase64Url { get; set; }
        
        
        public string ExchangeSalt64 { get; set; }
    }
}