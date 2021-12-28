using System;

namespace Youverse.Core.Services.Authentication.AppAuth
{
    public class AppDevice
    {
        public Guid ApplicationId { get; set; }
        public byte[] DeviceUid { get; set; }
    }
}