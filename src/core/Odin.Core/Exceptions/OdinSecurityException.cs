using System;
using System.Collections.Generic;

namespace Odin.Core.Exceptions
{
    public class OdinSecurityException : OdinException
    {
        public OdinSecurityException(string message) : base(message)
        {
        }

        public OdinSecurityException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public OdinSecurityException(string message, OdinClientErrorCode errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        /// <summary>
        /// Indicates this was due to a key decryption issue
        /// </summary>
        public bool IsRemoteIcrIssue { get; set; }

        public OdinClientErrorCode ErrorCode { get; set; } = OdinClientErrorCode.NoErrorCode;

        /// <summary>Written onto the problem details, as <see cref="OdinClientException.Extensions"/> is</summary>
        public Dictionary<string, object> Extensions { get; init; }
    }
}