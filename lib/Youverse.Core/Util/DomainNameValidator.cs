using DnsClient;
using DnsClient.Protocol;
using System;
using System.Linq;
using Youverse.Core.Trie;

//using DnsClient; // https://www.nuget.org/packages/DnsClient/

namespace Youverse.Core.Util
{
    // DNS name is {label.}+label
    //
    public class DomainName
    {
        private readonly string _punyDomainName;

        public const int MAX_DNS_LABEL = 63;  // as per DNS RFC
        public const int MAX_DNS_DOMAIN = 255;  // as per DNS RFC, max 255 characters in total


        public DomainName(string punyDomainName)
        {
            _punyDomainName = punyDomainName;
        }

    }


    // DNS name is {label.}+label. Max 254 characters total. Max 127 levels
    //
    public class DomainNameValidator
    {
        public const int MAX_DNS_LABEL_COUNT = 127;  // as per DNS RFC
        public const int MAX_DNS_LABEL_LENGTH = 63;  // as per DNS RFC
        public const int MAX_DNS_DOMAIN_LENGTH = 255;  // as per DNS RFC

        public static readonly byte[] punyCodeAsciiMap =
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
        public static bool TryValidateDomain(string punyDomainName)
        {
            if (punyDomainName is null)
                return false;

            if (punyDomainName.Length < 3 || punyDomainName.Length > MAX_DNS_DOMAIN_LENGTH)
                return false;

            int labelCount = 0;
            int labelLength = 0;

            for (int i=0; i < punyDomainName.Length; i++)
            {
                if (punyDomainName[i] > 255) // Illegal non-ASCII character
                    return false;

                if (punyCodeAsciiMap[punyDomainName[i]] == 128) // 128 is illegal punyCode character
                    return false;
                
                if (punyDomainName[i] == '.')
                {
                    if (labelLength == 0) // Label cannot be empty
                        return false;

                    labelCount++;

                    if (labelLength > MAX_DNS_LABEL_LENGTH) // Too many labels per RFC
                        return false;

                    if (punyDomainName[i-1] == '-') // Last label char cannot be hyphen
                        return false;

                    labelLength = 0;
                }
                else
                {
                    if (labelLength ==0)
                        if (punyDomainName[i] == '-') // First label char cannot be hyphen
                            return false;

                    labelLength++;
                }
            }

            labelCount++; // The last label

            // Check for last label's length and it should not end with hyphen
            if (labelLength == 0 || labelLength > MAX_DNS_LABEL_LENGTH || punyDomainName[punyDomainName.Length - 1] == '-')
                return false;

            return labelCount >= 2 && labelCount < MAX_DNS_LABEL_COUNT;
        }

        /*
        // Validate if a DNS *label* is OK
        // false not OK. true OK.
        public static bool ValidLabel(string punyCodeLabel)
        {
            if (punyCodeLabel.Length < 1 || punyCodeLabel.Length > MAX_DNS_LABEL_LENGTH)
                return false; // Too short or long

            // The first and last character cannot be the hyphen
            if ("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(punyCodeLabel[0]) == -1)
                return false; // Starts with illegal character

            IdnMapping idn = new IdnMapping();
            try
            {
                var _ = idn.GetUnicode(punyCodeLabel).ToUtf8ByteArray();
            }
            catch
            {
                return false;
            }

            var ln = punyCodeLabel.Length - 1;

            if ("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(punyCodeLabel[ln]) == -1)
                return false; // Ends with illegal character

            for (ln--; ln > 0; ln--)
                if ("-abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(punyCodeLabel[ln]) == -1)
                    return false; // Ends with illegal character

            return true;
        }
        */

        // Check the whole domain name. Throw an exception if it is invalid.

        public static void AssertValidDomain(string punyCodeDomain)
        {
            if (TryValidateDomain(punyCodeDomain) == false)
                throw new Exception("Illegal puny code domain name."); // Thrown an exception

            /*

            if (punyCodeDomain.Length > MAX_DNS_DOMAIN_LENGTH)
                throw new DomainTooLongException(); // Too long

            if (punyCodeDomain.Length < 3)
                throw new DomainTooShortException(); // Too short (a.a minimum)

            var labels = punyCodeDomain.Split('.');

            if (labels.Length < 2)
                throw new DomainNeedsTwoLabelsException(); // Need at least two labels

            for (var i = 0; i < labels.Length; i++)
                if (ValidLabel(labels[i]) == false)
                    throw new DomainIllegalCharacterException(); */
            // All clear
        }


        /* public static string GetCName(string sDomain)
        {
            var client = new LookupClient();
            var result = client.Query(sDomain, QueryType.CNAME);

            foreach (var aRecord in result.Answers)
            {
                Console.WriteLine(aRecord.DomainName);
                return aRecord.DomainName;
            }

            return "";
        }*/

        /*
            Console.WriteLine("Looking up www.dnsimple.com for CNAME");

            //DomainName.GetCName("www.dnsimple.com");

        */

        // MS implementation of simple IP lookup with
        // built-in DNS resolver. Probably not needed
        // or replace with DnsClient 
        /*
        public void IPLookup(string domainName)
        {
            // Yikes. Not DNS. See here: 
            var hostInfo = Dns.GetHostEntry(domainName);

            Console.WriteLine(hostInfo);
            for (var i = 0; i < hostInfo.AddressList.Length; i++) Console.WriteLine(hostInfo.AddressList[i]);

            Console.WriteLine("Aliases:");
            for (var i = 0; i < hostInfo.Aliases.Length; i++) Console.WriteLine(hostInfo.Aliases[i]);

            Console.WriteLine("Hostname: " + hostInfo.HostName);
        }*/

        // Given a domain name, looks up and returns the CNAME record.
        // If a CNAME record does not exist then it null
        //
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

        public static string CNameLookup2(string inputHostName)
        {
            var client = new LookupClient(NameServer.GooglePublicDns);
            var result = client.Query(inputHostName, QueryType.CNAME);
            var record = result.Answers.CnameRecords().First();

            return record.CanonicalName;
        }

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



/*
 *             var lookup = new LookupClient();
            var result = lookup.Query(inputHostName, QueryType.CNAME);
            var record = result.Answers.CnameRecords().FirstOrDefault();

            if (record == null)
                return null;
            else
                return record.CanonicalName;

*/