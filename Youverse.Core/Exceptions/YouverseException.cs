using System;

namespace Youverse.Core.Exceptions
{
    public class YouverseException : Exception
    {
        public YouverseException(string message) : base(message)
        {
        }

        public YouverseException(string message, Exception inner) : base(message, inner)
        {
        }        
    }
    
    
}
