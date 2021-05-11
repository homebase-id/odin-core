using DotYou.Types.Identity;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class NoteAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Note; } set { } }

        public override string ToString()
        {
            return this.Note;
        }

        [JsonProperty("note")]
        public string Note { get; set; }
    }
}
