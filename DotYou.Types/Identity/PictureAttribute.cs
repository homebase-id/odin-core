using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class PictureAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Picture; } set { } }

        public override string ToString()
        {
            return this.Picture;
        }

        [JsonProperty("picture")]
        public string Picture { get; set; }
    }
}
