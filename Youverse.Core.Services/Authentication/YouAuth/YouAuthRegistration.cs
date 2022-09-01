using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Contacts.Circle.Membership;

#nullable enable
namespace Youverse.Core.Services.Authentication.YouAuth
{
    public sealed class YouAuthRegistration
    {
        public DateTimeOffset CreatedAt { get; init; }
        public string Subject { get; init; }

        public SymmetricKeyEncryptedAes IcrRemoteKeyEncryptedKeyStoreKey { get; set; }

        public Dictionary<string, CircleGrant> CircleGrants { get; set; }


        public YouAuthRegistration(string subject, Dictionary<string, CircleGrant> circleGrants, SymmetricKeyEncryptedAes icrRemoteKeyEncryptedKeyStoreKey)
        {
            Subject = subject;
            CreatedAt = DateTimeOffset.Now;
            CircleGrants = circleGrants;
            IcrRemoteKeyEncryptedKeyStoreKey = icrRemoteKeyEncryptedKeyStoreKey;
        }
    }
}