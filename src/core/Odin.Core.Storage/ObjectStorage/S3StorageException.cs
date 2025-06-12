using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public class S3StorageException : OdinSystemException
{
    public S3StorageException(string message) : base(message)
    {
    }

    public S3StorageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
