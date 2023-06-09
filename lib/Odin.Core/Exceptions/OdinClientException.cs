using System;

namespace Odin.Core.Exceptions
{
    public class OdinClientException : OdinException
    {
        public OdinClientErrorCode ErrorCode { get; set; }

        public OdinClientException(string message, OdinClientErrorCode code = OdinClientErrorCode.UnhandledScenario) : base(message)
        {
            this.ErrorCode = code;
        }

        public OdinClientException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    public class OdinRemoteIdentityException : Exception
    {
        public OdinClientErrorCode ErrorCode { get; set; }

        public OdinRemoteIdentityException(string message, OdinClientErrorCode code = OdinClientErrorCode.UnhandledScenario) : base(message)
        {
            this.ErrorCode = code;
        }

        public OdinRemoteIdentityException(string message, Exception inner) : base(message, inner)
        {
        }
        
    }
}