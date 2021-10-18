using System;
using System.Diagnostics;
using System.Net;

//using DnsClient; // https://www.nuget.org/packages/DnsClient/

namespace Youverse.Core.Trie
{
    // DNS name is {label.}+label. Max 254 characters total. Max 127 levels
    //
    public class DomainName
    {
        // Validate if a DNS *label* is OK
        // false not OK. true OK.
        public static bool ValidLabel(string label)
        {
            if (label.Length < 1 || label.Length > 63)
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
            if (domain.Length > 253)
                throw new DomainTooLong(); // Too long

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


        /*
        public static string GetCName(string sDomain)
        {
            //var client = new LookupClient();
            var client = new LookupClient();
            var result = client.Query(sDomain, QueryType.CNAME);

            foreach (var aRecord in result.Answers)
            {
                Console.WriteLine(aRecord.DomainName);
                return aRecord.DomainName;
            }

            return "";
        }
        */


        // MS implementation of simple IP lookup with
        // built-in DNS resolver. Probably not needed
        // or replace with DnsClient 
        public void SampleLookup(string domain)
        {
            var hostInfo = Dns.GetHostEntry(domain);

            Console.WriteLine(hostInfo);
            Console.WriteLine("AddressList:");
            for (var i = 0; i < hostInfo.AddressList.Length; i++) Console.WriteLine(hostInfo.AddressList[i]);

            Console.WriteLine("Aliases:");
            for (var i = 0; i < hostInfo.Aliases.Length; i++) Console.WriteLine(hostInfo.Aliases[i]);

            Console.WriteLine("Hostname: " + hostInfo.HostName);
        }

        public void _Test()
        {
            // Test valid labels
            Debug.Assert(ValidLabel("") == false, "Empty name error");
            Debug.Assert(ValidLabel("012345678901234567890123456789012345678901234567890123456789012") == true,
                "63 chars not allowed");
            Debug.Assert(ValidLabel("0123456789012345678901234567890123456789012345678901234567890123") == false,
                "64 chars allowed");
            Debug.Assert(ValidLabel("-a") == false, "Allowed to start with -");
            Debug.Assert(ValidLabel("a-") == false, "Allowed to end with -");
            Debug.Assert(ValidLabel("a") == true, "one char not allowed");

            ValidateDomain("a.com");
            ValidateDomain(".com");
            ValidateDomain("a.");
            ValidateDomain("-a.com");
            ValidateDomain("a-.com");
            ValidateDomain("a.com-");
            ValidateDomain(".");
            ValidateDomain("..");
            ValidateDomain("...");

            Console.WriteLine("Looking up www.dnsimple.com for CNAME");

            //DomainName.GetCName("www.dnsimple.com");

            Console.WriteLine("Domain tests passed.");
        }
    }
}