using System.Collections.Generic;

namespace Odin.Services.Dns.PowerDns;

// https://doc.powerdns.com/authoritative/http-api/cryptokey.html
// NOTE: shape modelled from the PowerDNS API docs; validated against a live server as
// part of the DNSSEC live-verification checklist (docs/byod-dnssec-plan.md).
public class Cryptokey
{
    public string type { get; set; }        // "Cryptokey"
    public int id { get; set; }
    public string keytype { get; set; }     // "ksk" | "zsk" | "csk"
    public bool active { get; set; }
    public bool published { get; set; } = true; // absent on older servers => treated as published
    public string dnskey { get; set; }      // DNSKEY rdata presentation
    public List<string> ds { get; set; }    // DS presentation strings; only on ksk/csk
    public string algorithm { get; set; }   // e.g. "ECDSAP256SHA256"
    public int bits { get; set; }
}
