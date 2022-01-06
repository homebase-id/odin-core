using System;

namespace Youverse.Core.Exceptions
{
    public abstract class YouverseException : Exception
    {
        protected YouverseException(string message) : base(message)
        {
        }

        protected YouverseException(string message, Exception inner) : base(message, inner)
        {
        }        
    }
    
    
}
