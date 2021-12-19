using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Drive.Query
{
    internal class NoValidIndexException : YouverseException
    {
        public NoValidIndexException(Guid driveId): base($"No valid index for drive {driveId}")
        {
            
        }
        public NoValidIndexException(string message) : base(message)
        {
        }

        public NoValidIndexException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}