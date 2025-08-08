namespace JsonLd.Normalization
{
    internal class TermType
    {
        public static TermType NamedNode = new TermType(nameof(NamedNode));
        public static TermType BlankNode = new TermType(nameof(BlankNode));
        public static TermType Literal = new TermType(nameof(Literal));
        public static TermType DefaultGraph = new TermType(nameof(DefaultGraph));

        public string Value { get; init; }

        protected TermType(string value)
        {
            Value = value;
        }
    }

    internal class QuadItem
    {
        public const string RDF = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        public const string RDF_LANGSTRING = RDF + "langString";
        public const string XSD = "http://www.w3.org/2001/XMLSchema#";
        public const string XSD_STRING = XSD + "string";

        public TermType TermType { get; set; }
        public string Value { get; set; }

        public override string ToString()
        {
            return $"{TermType?.Value}: {Value}";
        }
    }

    internal class ObjectQuadItem : QuadItem
    {
        public QuadItem DataType { get; set; }
        public string Language { get; set; }

        public ObjectQuadItem() { }
        public ObjectQuadItem(QuadItem otherItem, ObjectQuadItem otherObj = null)
        {
            TermType = otherItem.TermType;
            Value = otherItem.Value;
            DataType = otherObj?.DataType;
            Language = otherObj?.Language;
        }

        public override string ToString()
        {
            return $"{DataType?.Value ?? TermType?.Value}: {Value}";
        }
    }

    internal class Quad
    {
        public QuadItem Subject { get; set; }
        public QuadItem Predicate { get; set; }
        public ObjectQuadItem Object { get; set; }
        public QuadItem Graph { get; set; }
    }
}
