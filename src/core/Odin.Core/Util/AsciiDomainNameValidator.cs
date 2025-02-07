using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using DnsClient;
using DnsClient.Protocol;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;

//using DnsClient; // https://www.nuget.org/packages/DnsClient/

namespace Odin.Core.Util
{
    // Guaranteed to hold a valid, lowercased puny domain name
    //
    [JsonConverter(typeof(AsciiDomainConverter))]
    public readonly struct AsciiDomainName : IGenericCloneable<AsciiDomainName>
    {
        // Provide a public property to read the puny domain
        public string DomainName { get; init; }

        public AsciiDomainName(string punyDomainName)
        {
            AsciiDomainNameValidator.AssertValidDomain(punyDomainName);
            DomainName = punyDomainName.ToLower();
        }

        public AsciiDomainName Clone()
        {
            return new AsciiDomainName
            {
                DomainName = DomainName
            };
        }

        // Get the IDN representation of the puny domain
        public string ToIDN()
        {
            var idnMapping = new IdnMapping();
            string unicode = idnMapping.GetUnicode(DomainName);
            return unicode;
        }

        // Static function to create a AsciiDomainName from an IDN
        public static AsciiDomainName FromIDN(string idnDomainName)
        {
            var idnMapping = new IdnMapping();
            string punyCode = idnMapping.GetAscii(idnDomainName);
            return new AsciiDomainName(punyCode);
        }

        public override string ToString()
        {
            return this.DomainName;
        }
    }


    // DNS name is {label.}+label. Max 254 characters total. Max 127 levels
    //
    public class AsciiDomainNameValidator
    {
        public const int MAX_DNS_LABEL_COUNT = 127;  // as per DNS RFC
        public const int MAX_DNS_LABEL_LENGTH = 63;  // as per DNS RFC
        public const int MAX_DNS_DOMAIN_LENGTH = 255;  // as per DNS RFC

        public static readonly byte[] CodeAsciiMap =
        {
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 000-009
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 010-019
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 020-029
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 030-039
            128, 128, 128, 128, 128,  26,  27, 128,  28,  29, // 040-049 '-' 45, '.' 46, '0' 48, '1' 49
             30,  31,  32,  33,  34,  35,  36,  37, 128, 128, // 050-059 ..'9' is 57
            128, 128, 128, 128, 128,   0,   1,   2,   3,   4, // 060-069 'A' is 65
              5,   6,   7,   8,   9,  10,  11,  12,  13,  14, // 070-079
             15,  16,  17,  18,  19,  20,  21,  22,  23,  24, // 080-089
             25, 128, 128, 128, 128, 128, 128,   0,   1,   2, // 090-099 'a' is 97
              3,   4,   5,   6,   7,   8,   9,  10,  11,  12, // 100-109
             13,  14,  15,  16,  17,  18,  19,  20,  21,  22, // 110-119
             23,  24,  25, 128, 128, 128, 128, 128, 128, 128, // 120-129 
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 130-139
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 140-149
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 150-159
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 160-169
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 170-179
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 180-189
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 190-199
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 200-209 
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 210-219
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 220-229
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 230-239
            128, 128, 128, 128, 128, 128, 128, 128, 128, 128, // 240-249
            128, 128, 128, 128, 128, 128                      // 250-255
        };

        // Not yet tested
        public static bool TryValidateDomain(string domainName)
        {
            if (domainName is null)
                return false;

            if (domainName.Length < 3 || domainName.Length > MAX_DNS_DOMAIN_LENGTH)
                return false;

            int labelCount = 0;
            int labelLength = 0;

            for (int i=0; i < domainName.Length; i++)
            {
                if (domainName[i] > 255) // Illegal non-ASCII character
                    return false;

                if (CodeAsciiMap[domainName[i]] == 128) // 128 is illegal punyCode character
                    return false;
                
                if (domainName[i] == '.')
                {
                    if (labelLength == 0) // Label cannot be empty
                        return false;

                    labelCount++;

                    if (labelLength > MAX_DNS_LABEL_LENGTH) // Too many labels per RFC
                        return false;

                    if (domainName[i-1] == '-') // Last label char cannot be hyphen
                        return false;

                    labelLength = 0;
                }
                else
                {
                    if (labelLength ==0)
                        if (domainName[i] == '-') // First label char cannot be hyphen
                            return false;

                    labelLength++;
                }
            }

            labelCount++; // The last label

            // Check for last label's length and it should not end with hyphen
            if (labelLength == 0 || labelLength > MAX_DNS_LABEL_LENGTH || domainName[domainName.Length - 1] == '-')
                return false;

            return labelCount >= 2 && labelCount < MAX_DNS_LABEL_COUNT;
        }


        // Check the whole domain name. Throw an exception if it is invalid.

        public static void AssertValidDomain(string punyCodeDomain)
        {
            if (TryValidateDomain(punyCodeDomain) == false)
            {
                throw new OdinClientException($"Illegal puny code domain name: '{punyCodeDomain}'"); // Thrown an exception
            }
        }


        // Given a domain name, looks up and returns the CNAME record.
        // If a CNAME record does not exist then it null
        //

        [Obsolete("This class must use an injected LookupClient or risk leaking resources")]
        public static string CNameLookup(string inputHostName)
        {
            var lookup = new LookupClient();
            var result = lookup.Query(inputHostName, QueryType.SRV);

            var srvRecord = result
                .Answers.OfType<CNameRecord>()
                .FirstOrDefault();

            if (srvRecord == null)
                return null;

            return srvRecord.CanonicalName;
        }

        [Obsolete("This class must use an injected LookupClient or risk leaking resources")]
        public static string CNameLookup2(string inputHostName)
        {
            var client = new LookupClient(NameServer.GooglePublicDns);
            var result = client.Query(inputHostName, QueryType.CNAME);
            var record = result.Answers.CnameRecords().First();

            return record.CanonicalName;
        }

        [Obsolete("This class must use an injected LookupClient or risk leaking resources")]
        public static void TryIdentityDNSValidate(string identityName)
        {
            const string serverHostFQDN = "odin.earth."; // Pick a config value

            if (CNameLookup2(identityName) != serverHostFQDN)
                throw new Exception("You do not have a CNAME record for "+identityName+" pointing to "+serverHostFQDN);

            // We are probably going to change the server CDN host... ?
            if (CNameLookup2("cdn." + identityName) != serverHostFQDN)
                throw new Exception("You do not have a CNAME record for " + identityName + " pointing to " + serverHostFQDN);
        }
    }
}