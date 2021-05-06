using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class FaceBookAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.FaceBook; } set { } }

        public override string ToString()
        {
            return this.FaceBook;
        }

        [JsonProperty("facebook")]
        public string FaceBook { get; set; }
    }
}
