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
    }
}