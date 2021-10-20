using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppDeviceRegistrationReply
    {
        public Guid Id { get; set; }
        
        public byte[] DeviceAppToken { get; set; }  // This is half the AppDek

        public byte[] SharedSecret { get; set; } // The secret shared with the client. We need one per client


    }
}