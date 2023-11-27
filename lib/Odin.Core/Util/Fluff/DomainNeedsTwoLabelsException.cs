using System;
using Odin.Core.Exceptions;

namespace Odin.Core.Util.Fluff
{
    internal class DomainNeedsTwoLabelsException : OdinSystemException
    {
        public DomainNeedsTwoLabelsException(string message) : base(message)
        {
        }

        public DomainNeedsTwoLabelsException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}