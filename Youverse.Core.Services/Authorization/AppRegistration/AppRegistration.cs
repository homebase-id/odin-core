using System;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
    public class AppRegistration
    {
        public Guid Id { get; internal set; }

        public Guid ApplicationId { get; set; }
        public string Name { get; set; }

        public byte[] EncryptedAppDeK { get; set; }

        public byte[] AppIV { get; set; }
        
        public bool IsRevoked { get; set; }
    }
}