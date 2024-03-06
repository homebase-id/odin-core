using Odin.Core.Exceptions;

namespace Odin.Services.Email;

public class EmailException : OdinException
{
    public EmailException(string message) : base(message)
    {
    }
}