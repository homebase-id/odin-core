using DotYou.Kernel.Cryptography;
using System;

namespace DotYou.Types
{
    /// <summary>
    /// Holds the tokens required when a device has been authenticated.  These should be
    /// different than the Authentication token
    /// </summary>
    public class DeviceAuthenticationResult
    {
        //TODO determine this during my next meetup with Michael
        public Guid DeviceToken { get; set; }
        
        public DotYouAuthenticationResult AuthenticationResult { get; set; }
        
    }
    
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
            return $"{SessionToken}{SEPARATOR}{ClientHalfKek}";
        }

        public static DotYouAuthenticationResult Parse(string value)
        {
            var arr = value.Split(SEPARATOR);
            return new DotYouAuthenticationResult()
            {
                SessionToken = Guid.Parse(arr[0]),
                ClientHalfKek = new SecureKey(Guid.Parse(arr[1]).ToByteArray()) //TODO: need to convert to base64 encoding correctly.  this will fail if we change the byte array to anything other that 16 bytes
            };
        }
        
        public static bool TryParse(string value, out DotYouAuthenticationResult result)
        {
            result = null;
            if (null == value)
            {
                return false;
            }
            
            var arr = value.Split(SEPARATOR);
            Guid t1;
            Guid t2;

            if (Guid.TryParse(arr[0], out t1) && Guid.TryParse(arr[1], out t2))
            {
                result = new DotYouAuthenticationResult()
                {
                    SessionToken = t1,
                    ClientHalfKek = new SecureKey(t2.ToByteArray()) //TODO: fix parsing to not use a guid but rather parse the string to a byte array
                };
                
                return true;
            }

            return false;
        }
    }
}