using System;

namespace Youverse.Core.Services.Authorization.Apps
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
        /// RSA encrypted response. If encryption version == 1, the ClientKek is the frist 16 bytes, SharedSecret is the second 16 bytes
        /// </summary>
        public byte[] Data { get; set; }
    }
}