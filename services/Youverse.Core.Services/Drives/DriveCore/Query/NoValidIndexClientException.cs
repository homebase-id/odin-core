using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Drive.Core.Query
{
    internal class NoValidIndexClientException : YouverseSystemException
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