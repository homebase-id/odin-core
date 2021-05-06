using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class EmailAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Email; } set { } }

        public override string ToString()
        {
            return this.Email;
        }

        [JsonProperty("email")]
        public string Email { get; set; }
    }
}
