using System;

namespace Youverse.Core.Storage
{
    public class UniqueIndexException : Exception
    {
        public UniqueIndexException()
        {
        }

        public UniqueIndexException(string message) : base(message)
        {
        }

        public UniqueIndexException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}