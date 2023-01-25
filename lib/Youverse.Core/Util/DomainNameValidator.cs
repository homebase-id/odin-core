using DnsClient;
using DnsClient.Protocol;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Youverse.Core.Trie;

//using DnsClient; // https://www.nuget.org/packages/DnsClient/

namespace Youverse.Core.Util
{
    // DNS name is {label.}+label. Max 254 characters total. Max 127 levels
    //
    public class DomainNameValidator
    {
        public const int MAX_DNS_LABEL  =  63;  // as per DNS RFC
        public const int MAX_DNS_DOMAIN = 253;  // as per DNS RFC, max 254 characters in total


        // Validate if a DNS *label* is OK
        // false not OK. true OK.
        public static bool ValidLabel(string label)
        {
            if (label.Length < 1 || label.Length > MAX_DNS_LABEL)
                return false; // Too short or long

            // The first and last character cannot be the hyphen
            if ("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(label[0]) == -1)
                return false; // Starts with illegal character

            var ln = label.Length - 1;

            if ("abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(label[ln]) == -1)
                return false; // Ends with illegal character

            for (ln--; ln > 0; ln--)
                if ("-abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(label[ln]) == -1)
                    return false; // Ends with illegal character

            return true;
        }

        // Check the whole domain name. Throw an exception if it is invalid.
        public static void ValidateDomain(string domain)
        {
            if (domain.Length > MAX_DNS_DOMAIN)
                throw new DomainTooLongException(); // Too long

            if (domain.Length < 3)
                throw new DomainTooShortException(); // Too short (a.a minimum)

            var labels = domain.Split('.');

            if (labels.Length < 2)
                throw new DomainNeedsTwoLabelsException(); // Need at least two labels

            for (var i = 0; i < labels.Length; i++)
                if (ValidLabel(labels[i]) == false)
                    throw new DomainIllegalCharacterException();
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