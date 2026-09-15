using System;
using System.Collections.Generic;

namespace Odin.Core.Exceptions
{
    public class OdinClientException : OdinException
    {
        public OdinClientErrorCode ErrorCode { get; set; }

        /// <summary>
        /// Extra fields handed to the client alongside the error code, copied onto the response's
        /// problem details.
        /// </summary>
        /// <remarks>
        /// For errors the client has to <i>act</i> on rather than merely display -- a rejection that
        /// names what to fix, where making the user discover the offenders one failed attempt at a
        /// time is the alternative.  Null for the overwhelming majority of errors, where the code and
        /// the message are the whole story.  Keys must not collide with the reserved
        /// <c>errorCode</c>, <c>correlationId</c> and <c>stackTrace</c>, which always win.
        /// </remarks>
        public Dictionary<string, object> Extensions { get; init; }

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