using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrantRedux;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistration
    {
        public Guid Id { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string Subject { get; init; }

        public IExchangeGrant Grant { get; set; }

        public YouAuthRegistration()
        {
            //for litedb
        }

        public YouAuthRegistration(Guid id, string subject, IExchangeGrant grant)
        {
            Id = id;
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            Grant = grant;
        }
    }
}