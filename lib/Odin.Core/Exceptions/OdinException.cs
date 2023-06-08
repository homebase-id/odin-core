using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Exceptions;

public abstract class OdinException : Exception
{
    public OdinException(string message) : base(message)
    {
    }

    public OdinException(string message, Exception inner) : base(message, inner)
    {
    }        
    
    protected OdinException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}
