#nullable enable
using System;
using System.Collections.Generic;
using Odin.Core.Services.Contacts.Circle.Membership;

namespace Odin.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistration
    {
        public DateTimeOffset CreatedAt { get; init; }
        public string Subject { get; init; }

        public Dictionary<string, CircleGrant> CircleGrants { get; set; }

        public YouAuthRegistration(string subject, Dictionary<string, CircleGrant> circleGrants)
        {
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            CircleGrants = circleGrants;
            
        }
    }
}