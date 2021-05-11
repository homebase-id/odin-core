using DotYou.Types.Identity;
using Newtonsoft.Json;

namespace Identity.DataType.Attributes
{
    public class AddressAttribute : BaseAttribute
    {
        public override int AttributeType { get { return (int)AttributeTypes.Address; } set { } }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(this.DisplayAddress) || string.IsNullOrWhiteSpace(this.DisplayAddress))
                return this.Street + " " + this.Extension + "\n" +
                       this.POBox + "\n" +
                       this.Locality + " " + this.PostCode + " " + this.Region + "\n" +
                       this.Country + "\n" +
                       this.Coordinates;
            else
                return this.DisplayAddress;
        }

        [JsonProperty("displayAddress")]
        public string DisplayAddress { get; set; }

        [JsonProperty("street")]
        public string Street { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }

        [JsonProperty("locality")]
        public string Locality { get; set; }

        [JsonProperty("region")]
        public string Region { get; set; }

        [JsonProperty("postOfficeBox")]
        public string POBox { get; set; }

        [JsonProperty("postCode")]
        public string PostCode { get; set; }

        [JsonProperty("country")]
        public string Country { get; set; }

        [JsonProperty("coordinates")]
        public string Coordinates { get; set; }
    }
}
