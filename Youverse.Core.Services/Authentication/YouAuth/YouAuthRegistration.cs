using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistration
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string Subject { get; init; }

        public ExchangeGrant Grant { get; set; }

        public YouAuthRegistration()
        {
            //for litedb
        }

        public YouAuthRegistration(Guid id, string subject, ExchangeGrant grant)
        {
            Id = id;
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            Grant = grant;
        }
    }
}