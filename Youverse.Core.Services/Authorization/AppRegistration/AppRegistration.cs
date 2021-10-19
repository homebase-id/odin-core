using System;
using LiteDB;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
    public class AppRegistration
    {
        public Guid Id { get; internal set; }

        public Guid ApplicationId { get; set; }
        public string Name { get; set; }

        public byte[] EncryptedAppDeK { get; set; }

        public byte[] AppIV { get; set; }
    }

    public class AppDeviceRegistration
    {
        public Guid Id { get; set; }
        
        public Guid ApplicationId { get; set; }

        public byte[] HalfAdek { get; set; } // Random 16-byte client cookie needed to calculate the application DeK

        public byte[] SharedSecret { get; set; } // The secret shared with the client. We need one per client
        public bool IsRevoked { get; set; }
        
    }
    
    public class AppDeviceRegistrationReply
    {
        public Guid Id { get; set; }
        
        public byte[] DeviceAppToken { get; set; }  // This is half the AppDek

        public byte[] SharedSecret { get; set; } // The secret shared with the client. We need one per client


    }
}