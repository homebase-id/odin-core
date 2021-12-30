using System;

namespace Youverse.Core.Services.Authentication.AppAuth
{
    public sealed class AppAuthSession
    {

        //
        public AppAuthSession()
        {
            
        }
        
        public AppAuthSession(Guid id, AppDevice appDevice, TimeSpan lifetime)
        {
            Id = id;
            AppDevice = appDevice;
            CreatedAt = DateTimeExtensions.UnixTimeMilliseconds();
            ExpiresAt = CreatedAt + (UInt64) lifetime.TotalMilliseconds;
        }

        public Guid Id { get; init; }
        public UInt64 CreatedAt { get; init; }
        public UInt64 ExpiresAt { get; init; }
        public AppDevice AppDevice { get; init; }

        public bool HasExpired() => DateTimeExtensions.UnixTimeMilliseconds() > ExpiresAt;
    }
}