using System;

namespace Youverse.Core.Exceptions
{
    public class ConfigException : YouverseException
    {
        public ConfigException(string message) : base(message)
        {
        }

        public ConfigException(string message, Exception inner) : base(message, inner)
        {
        }        
    }
}
