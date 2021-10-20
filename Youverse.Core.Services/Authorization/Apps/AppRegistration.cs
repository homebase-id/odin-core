using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistration
    {
        public Guid Id { get; set; }

        public Guid ApplicationId { get; set; }
        public string Name { get; set; }

        public byte[] EncryptedAppDeK { get; set; }

        public byte[] AppIV { get; set; }
        
        public bool IsRevoked { get; set; }
    }
}