﻿using System;
using System.Runtime.Serialization;

namespace DotYou.IdentityRegistry
{
    [Serializable]
    public class DomainIllegalCharacterException : Exception
    {
        public DomainIllegalCharacterException()
        {
        }

        public DomainIllegalCharacterException(string message) : base(message)
        {
        }

        public DomainIllegalCharacterException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DomainIllegalCharacterException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}