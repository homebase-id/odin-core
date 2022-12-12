﻿using System;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Represents the client parts of the <see cref="ClientAccessToken"/> sent from the client during each request. 
    /// </summary>
    public class ClientAuthenticationToken
    {
        private static string SEPARATOR = "|";

        public ClientAuthenticationToken()
        {
            ClientTokenType = ClientTokenType.Other;
        }

        /// <summary>
        /// The login session's Id
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The Client's 1/2 of the KeK
        /// </summary>
        public SensitiveByteArray AccessTokenHalfKey { get; set; }

        public ClientTokenType ClientTokenType { get; set; }

        public override string ToString()
        {
            var data = ToPortableBytes();
            return Convert.ToBase64String(data);
        }

        public byte[] ToPortableBytes()
        {
            var data = ByteArrayUtil.Combine(this.Id.ToByteArray(), AccessTokenHalfKey.GetKey(), new[] { (byte)this.ClientTokenType });
            return data;
        }

        public Guid AsKey()
        {
            return new Guid(ByteArrayUtil.EquiByteArrayXor(Id.ToByteArray(), AccessTokenHalfKey.GetKey()));
        }

        public static ClientAuthenticationToken FromPortableBytes(byte[] data)
        {
            var (idBytes, secondSet) = ByteArrayUtil.Split(data, 16, 17);
            var (halfKeyBytes, type) = ByteArrayUtil.Split(secondSet, 16, 1);

            return new ClientAuthenticationToken()
            {
                Id = new Guid(idBytes),
                AccessTokenHalfKey = halfKeyBytes.ToSensitiveByteArray(),
                ClientTokenType = (ClientTokenType)type[0]
            };
        }

        public static ClientAuthenticationToken Parse(string value64)
        {
            var data = Convert.FromBase64String(value64);
            return FromPortableBytes(data);
        }

        public static bool TryParse(string value, out ClientAuthenticationToken result)
        {
            result = null;
            if (null == value)
            {
                return false;
            }

            try
            {
                result = Parse(value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}