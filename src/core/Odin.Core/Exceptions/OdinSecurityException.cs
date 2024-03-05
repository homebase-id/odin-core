using System;

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

        /// <summary>
        /// Indicates this was due to a key decryption issue
        /// </summary>
        public bool IsRemoteIcrIssue { get; set; }
    }
}