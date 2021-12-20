using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Drive
{
    public class InvalidDriveException : YouverseException
    {
        public InvalidDriveException(Guid driveId): base($"No valid index for drive {driveId}")
        {
            
        }
        public InvalidDriveException(string message) : base(message)
        {
        }

        public InvalidDriveException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}