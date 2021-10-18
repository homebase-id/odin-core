using Newtonsoft.Json;

namespace Youverse.Core.Identity.DataAttribute
{
    public class NameAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Name; } set { } }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(this.FullName) || string.IsNullOrWhiteSpace(this.FullName))
                return this.Prefix + " " + this.Personal + " " + this.Surname + " " + this.Additional + " " +
                       this.Suffix;
            else
                return this.FullName;
        }

        [JsonProperty("prefix")]
        public string Prefix { get; set; }

        [JsonProperty("personal")]
        public string Personal { get; set; }

        [JsonProperty("additional")]
        public string Additional { get; set; }

        [JsonProperty("surname")]
        public string Surname { get; set; }

        [JsonProperty("suffix")]
        public string Suffix { get; set; }

        [JsonProperty("fullName")]
        public string FullName { get; set; }
    }
}
