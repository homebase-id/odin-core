using System;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// Represents the client parts of the <see cref="ClientAccessToken"/> sent from the client during each request. 
    /// </summary>
    public class ClientAuthenticationToken
    {
        private static string SEPARATOR = "|";
        
        /// <summary>
        /// The login session's Id
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The Client's 1/2 of the KeK
        /// </summary>
        public SensitiveByteArray AccessTokenHalfKey { get; set; }

        public override string ToString()
        {
            string b64 = Convert.ToBase64String(AccessTokenHalfKey.GetKey());
            return $"{Id}{SEPARATOR}{b64}";
        }

        public static ClientAuthenticationToken Parse(string value)
        {
            var arr = value.Split(SEPARATOR);
            return new ClientAuthenticationToken()
            {
                Id = Guid.Parse(arr[0]),
                AccessTokenHalfKey = new SensitiveByteArray(arr[1]) 
            };
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