using System.Collections.Generic;

namespace Odin.Services.Dns.PowerDns;

public class ZoneWithRecords
{
    public string account { get; set; }
    public bool api_rectify { get; set; }
    public bool dnssec { get; set; }
    public int edited_serial { get; set; }
    public string id { get; set; }
    public string kind { get; set; }
    public int last_check { get; set; }
    public List<object> master_tsig_key_ids { get; set; }
    public List<object> masters { get; set; }
    public string name { get; set; }
    public int notified_serial { get; set; }
    public bool nsec3narrow { get; set; }
    public string nsec3param { get; set; }
    public List<Rrset> rrsets { get; set; }
    public int serial { get; set; }
    public List<object> slave_tsig_key_ids { get; set; }
    public string soa_edit { get; set; }
    public string soa_edit_api { get; set; }
    public string url { get; set; }    
    
    public class Rrset
    {
        public List<object> comments { get; set; }
        public string name { get; set; }
        public List<Record> records { get; set; }
        public int ttl { get; set; }
        public string type { get; set; }
    }
    
    public class Record
    {
        public string content { get; set; }
        public bool disabled { get; set; }        
    }
}

