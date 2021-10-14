using Newtonsoft.Json;

namespace DotYou.Types.DataAttribute
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
