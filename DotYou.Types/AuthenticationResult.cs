using System;

namespace DotYou.Types
{
    public class AuthenticationResult
    {
        private static string SEPARATOR = "|";
        
        public Guid Token { get; set; }

        /// <summary>
        /// The Client's 1/2 of the KeK
        /// </summary>
        public Guid Token2 { get; set; }

        public override string ToString()
        {
            return $"{Token}{SEPARATOR}{Token2}";
        }

        public static AuthenticationResult Parse(string value)
        {
            var arr = value.Split(SEPARATOR);
            return new AuthenticationResult()
            {
                Token = Guid.Parse(arr[0]),
                Token2 = Guid.Parse(arr[1])
            };
        }
        
        public static bool TryParse(string value, out AuthenticationResult result)
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
                result = new AuthenticationResult()
                {
                    Token = t1,
                    Token2 = t2
                };
                
                return true;
            }

            return false;
        }
    }
}