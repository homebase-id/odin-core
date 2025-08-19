/**
 * Parts of the source code in this file has been translated/ported from jsonld.js library by Digital Bazaar (BSD 3-Clause license)
*/

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace JsonLd.Normalization
{
    public class ExpandContext
    {
        public Dictionary<string, JObject> Mappings { get; init; } = new();
        public Dictionary<string, bool> Protected { get; init; } = new();
        public object Inverse { get; set; }
        public Dictionary<string, JToken> Fields { get; init; } = new();
        public ExpandContext PreviousContext { get; set; }

        public object CreateInverseContext()
        {
            throw new NotImplementedException();
        }

        public ExpandContext CloneActiveContext()
        {
            var child = new ExpandContext();
            foreach (var kvp in Mappings)
                child.Mappings[kvp.Key] = (JObject)kvp.Value.DeepClone();
            child.Inverse = null;
            //child.clone = this.clone; //CloneActiveContext
            //child.getInverse = this.getInverse; //CreateInverseContext
            //child.revertToPreviousContext = this.revertToPreviousContext;

            foreach (var kvp in Protected)
                child.Protected[kvp.Key] = kvp.Value;

            if (PreviousContext is not null)
                child.PreviousContext = PreviousContext.CloneActiveContext();

            if (Fields.TryGetValue("@base", out var baseField))
                child.Fields["@base"] = baseField.DeepClone();
            if (Fields.TryGetValue("@language", out var languageField))
                child.Fields["@language"] = languageField.DeepClone();
            if (Fields.TryGetValue("@vocab", out var vocabField))
                child.Fields["@vocab"] = vocabField.DeepClone();

            return child;
        }

        public ExpandContext RevertToPreviousContext()
        {
            if (PreviousContext is null)
                return this;

            return PreviousContext.CloneActiveContext();
        }
    }
}
