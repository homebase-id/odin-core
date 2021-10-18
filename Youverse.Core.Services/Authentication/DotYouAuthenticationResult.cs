using System;
using Youverse.Core.Cryptography;

namespace Youverse.Core.Services.Authentication
{
    public class DotYouAuthenticationResult
    {
        private static string SEPARATOR = "|";
        
        /// <summary>
        /// The login session's Id
        /// </summary>
        public Guid SessionToken { get; set; }

        /// <summary>
        /// The Client's 1/2 of the KeK
        /// </summary>
        public SecureKey ClientHalfKek { get; set; }

        public override string ToString()
        {
            string b64 = Convert.ToBase64String(ClientHalfKek.GetKey());
            return $"{SessionToken}{SEPARATOR}{b64}";
        }

        public static DotYouAuthenticationResult Parse(string value)
        {
            var arr = value.Split(SEPARATOR);
            return new DotYouAuthenticationResult()
            {
                SessionToken = Guid.Parse(arr[0]),
                ClientHalfKek = new SecureKey(arr[1]) 
            };
        }
        
        public static bool TryParse(string value, out DotYouAuthenticationResult result)
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