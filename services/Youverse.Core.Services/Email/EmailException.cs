using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Email;

public class EmailException : OdinException
{
    public EmailException(string message) : base(message)
    {
    }
}