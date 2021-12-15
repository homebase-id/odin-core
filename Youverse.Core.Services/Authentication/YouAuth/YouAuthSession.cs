using System;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthSession
    {
        public Guid Id { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset ExpiresAt { get; }
        public string Subject { get; }

        public YouAuthSession(Guid id, string subject, TimeSpan lifetime)
        {
            Id = id;
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            ExpiresAt = CreatedAt + lifetime;
        }

        public bool HasExpired => DateTimeOffset.Now > ExpiresAt;
    }
}