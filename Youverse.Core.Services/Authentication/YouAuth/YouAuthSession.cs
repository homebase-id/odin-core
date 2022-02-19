using System;
using Youverse.Core.Services.Authorization.Exchange;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthSession
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public string Subject { get; init; }
        public XToken XToken { get; init; }

        public YouAuthSession()
        {
            //for litedb
        }

        public YouAuthSession(Guid id, string subject, TimeSpan lifetime, XToken xToken)
        {
            Id = id;
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            ExpiresAt = CreatedAt + lifetime;
            XToken = xToken;

        }

        public bool HasExpired() => DateTimeOffset.Now > ExpiresAt;
    }
}