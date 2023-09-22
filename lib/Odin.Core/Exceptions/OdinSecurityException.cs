using System;
using System.Runtime.Serialization;

namespace Odin.Core.Exceptions
{
    public class OdinSecurityException : OdinException
    {
        public OdinSecurityException():base("")
        {
        }

        public OdinSecurityException(string message) : base(message)
        {
        }

        public OdinSecurityException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected OdinSecurityException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }

        /// <summary>
        /// Indicates this was due to a key decryption issue
        /// </summary>
        public bool IsRemoteIcrIssue { get; set; }
    }
}