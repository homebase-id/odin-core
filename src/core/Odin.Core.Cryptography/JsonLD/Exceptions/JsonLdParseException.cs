using System;

namespace JsonLd.Normalization
{
    public class JsonLdParseException : Exception
    {
        public string Line { get; init; }
        public int LineNumber { get; init; }

        public JsonLdParseException(string message, Exception innerException = null) : base(message, innerException)
        {
            Line = null;
            LineNumber = -1;
        }
        public JsonLdParseException(string line, int lineNumber) 
            : base($"N-Quads parse error on line {lineNumber}.")
        {
            Line = line;
            LineNumber = lineNumber;
        }
    }
}
