using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Drive
{
    public class InvalidDriveException : StorageException
    {
        public InvalidDriveException(Guid driveId): base($"No valid drive with Id: {driveId}")
        {
        }
    }
    
    public class StorageException : YouverseException
    {
        public StorageException(Guid driveId): base($"General Storage Exception regarding drive Id: {driveId}")
        {
            
        }
        
        public StorageException(string message) : base(message)
        {
        }

        public StorageException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
