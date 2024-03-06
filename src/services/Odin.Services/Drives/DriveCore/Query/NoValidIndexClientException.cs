using System;
using Odin.Core.Exceptions;

namespace Odin.Services.Drives.DriveCore.Query
{
    internal class NoValidIndexClientException : OdinSystemException
    {
        public NoValidIndexClientException(Guid driveId): base($"No valid index for drive {driveId}")
        {
            
        }
        public NoValidIndexClientException(string message) : base(message)
        {
        }

        public NoValidIndexClientException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}