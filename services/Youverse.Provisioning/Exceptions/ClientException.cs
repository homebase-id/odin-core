namespace Youverse.Provisioning.Exceptions;

public class ClientException : Exception
{
    public ClientErrorCode ErrorCode { get; set; }

    public ClientException(string message, ClientErrorCode code = ClientErrorCode.Todo) : base(message)
    {
        this.ErrorCode = code;
    }

    public ClientException(string message, Exception inner) : base(message, inner)
    {
    }
}

public class ServerException : Exception
{
    public ServerException(string message, Exception inner = null) : base(message, inner)
    {
    }
}