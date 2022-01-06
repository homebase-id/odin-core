using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    /// <summary>
    /// Data sent to client when registering the user of an app on a given device (phone, browser, etc.)
    /// </summary>
    public class AppDeviceRegistrationResponse
    {
        /// <summary>
        /// Used to lookup the server half of the app's Dek
        /// </summary>
        public Guid Token { get; set; }
        
        public byte[] DeviceSecret { get; set; }  // This is half the AppDek
        

    }
}