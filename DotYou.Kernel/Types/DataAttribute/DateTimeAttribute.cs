using Newtonsoft.Json;

namespace DotYou.Types.DataAttribute
{
    public class DateTimeAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.DateTime; } set { } }

        public override string ToString()
        {
            return this.DateTime;
        }

        [JsonProperty("dateTime")]
        public string DateTime { get; set; }
    }
}
