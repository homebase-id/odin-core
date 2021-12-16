using System;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthSession
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public string Subject { get; init;  }

        public YouAuthSession(Guid id, string subject, TimeSpan lifetime)
        {
            Id = id;
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            ExpiresAt = CreatedAt + lifetime;
        }

        public bool HasExpired() => DateTimeOffset.Now > ExpiresAt;
    }
}