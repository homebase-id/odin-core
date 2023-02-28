using System;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Drives
{
    public class InvalidDriveClientException : YouverseClientException
    {
        public InvalidDriveClientException(Guid driveId): base($"No valid drive with Id: {driveId}")
        {
        }
    }
}
