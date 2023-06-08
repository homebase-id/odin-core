using Odin.Core.Exceptions;

namespace Odin.Core.Services.Email;

public class EmailException : OdinException
{
    public EmailException(string message) : base(message)
    {
    }
}