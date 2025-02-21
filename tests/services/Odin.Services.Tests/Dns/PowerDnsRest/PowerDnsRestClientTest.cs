using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using DnsClient;
using HttpClientFactoryLite;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Dns;
using Odin.Services.Dns.PowerDns;

namespace Odin.Services.Tests.Dns.PowerDnsRest;

#nullable enable

public class PowerDnsRestClientTest
{
    private const string PdnsHostAddess = "dns.id.pub";
    private const string PdnsApiKey = "replace_with_top_secret_powerdns_api_key";
    private readonly IDnsRestClient _pdnsClient;

    private const string Ns1 = "ns1.id.pub.";
    private const string Ns2 = "ns2.id.pub.";
    private const string IpNs1 = "167.71.35.44";
    private const string IpNs2 = "164.92.193.199";
    
    public PowerDnsRestClientTest()
    {
        var logger = new Mock<ILogger<PowerDnsRestClient>>().Object;

        var httpClientFactory = new HttpClientFactory();
        var baseAddress = new Uri($"https://{PdnsHostAddess}/api/v1");
        _pdnsClient = new PowerDnsRestClient(logger, httpClientFactory, baseAddress, PdnsApiKey);
    }
    
    //

    [Test, Explicit]
    public async Task ItShouldGetAllZones()
    {
        var zones = await _pdnsClient.GetZones();
        Assert.That(zones.Any(x => x.id == "id.pub."), Is.True);
    }
    
    //
    
    [Test, Explicit]
    public async Task ItShouldGetIdPubZone()
    {
        var zone = await _pdnsClient.GetZone("id.pub.");
        ClassicAssert.AreEqual("id.pub.", zone.id);
        ClassicAssert.AreEqual("Native", zone.kind);
        ClassicAssert.AreEqual("id.pub.", zone.name);
        Assert.That(zone.url.EndsWith("zones/id.pub."), Is.True);
        ClassicAssert.GreaterOrEqual(zone.rrsets.Count, 3);
        
        // Test SOA record
        var soaRecords = zone.rrsets.Where(x => x.type == "SOA").Single(x => x.name == "id.pub.");
        Assert.That(soaRecords.records.Any(x => x.content.StartsWith(Ns1)), Is.True);

        // Test ns1 A record
        var ns1RrsetARecords = zone.rrsets.Where(x => x.type == "A").Single(x => x.name == Ns1);
        var ns1IpAddress = ns1RrsetARecords.records.Single().content; 
        ClassicAssert.AreEqual(IpNs1, ns1IpAddress);

        // Test ns2 A record
        var ns2RrsetARecords = zone.rrsets.Where(x => x.type == "A").Single(x => x.name == Ns2);
        var ns2IpAddress = ns2RrsetARecords.records.Single().content;
        ClassicAssert.AreEqual(IpNs2, ns2IpAddress);
        
        // Test NS records
        var nsRecords = zone.rrsets.Single(x => x.type == "NS" && x.name == "id.pub.");
        Assert.That(nsRecords.records.Any(x => x.content == Ns1));
        Assert.That(nsRecords.records.Any(x => x.content == Ns2));
    }

    //
    
    [Test, Explicit]
    public async Task ItShouldCreateAndDeleteAZone()
    {
        var domainName = $"unit-test.{Guid.NewGuid()}.id.pub"; 
        var zoneId = domainName + ".";
        
        //
        // Create zone
        //
        var newZone = await _pdnsClient.CreateZone(zoneId, new [] { Ns1, Ns2 }, "sebbarg@gmail.com");
        
        ClassicAssert.AreEqual(zoneId, newZone.name);
        ClassicAssert.AreEqual(zoneId, newZone.id);
        ClassicAssert.AreEqual(2, newZone.rrsets.Count); // 1 SOA, 2 NS
        
        //
        // Verify SOA is present using our own primary name server
        //
        {
            var client = new LookupClient(IPAddress.Parse(IpNs1));
            var result = await client.QueryAsync(zoneId, QueryType.SOA);
            var records = result.Answers.SoaRecords().ToList();
            ClassicAssert.AreEqual(1, records.Count);
            ClassicAssert.AreEqual(zoneId, records[0].DomainName.ToString());
        }
        
        //
        // Delete zone
        //
        await _pdnsClient.DeleteZone(zoneId);

        //
        // Verify SOA is no longer present using our own primary name server
        //
        {
            var client = new LookupClient(IPAddress.Parse(IpNs1));
            var result = await client.QueryAsync(zoneId, QueryType.SOA);
            var records = result.Answers.SoaRecords().ToList();
            ClassicAssert.AreEqual(0, records.Count);
        }
    }
    
    //

    [Test, Explicit]
    public async Task ItShouldCreateAndDeleteAnARecord()
    {
        var domainName = $"unit-test.{Guid.NewGuid()}.id.pub";
        var zoneId = domainName + ".";
        var recordName = "my.a.records";
        var fqRecordName = $"{recordName}.{domainName}";
        
        await _pdnsClient.CreateZone(zoneId, new [] { Ns1, Ns2 }, "sebbarg@gmail.com");
        try
        {
            await _pdnsClient.CreateARecords(zoneId, recordName, new [] { "127.0.0.1", "127.0.0.2" });
            
            //
            // Verify A is present using our own primary name server
            //
            {
                var client = new LookupClient(IPAddress.Parse(IpNs1));
                var result = await client.QueryAsync(fqRecordName, QueryType.A);
                var records = result.Answers.ARecords().ToList();
                ClassicAssert.AreEqual(2, records.Count);
            }
            
            await _pdnsClient.DeleteARecords(zoneId, recordName);
            
            //
            // Verify A is no longer present using our own primary name server
            //
            {
                var client = new LookupClient(IPAddress.Parse(IpNs1));
                var result = await client.QueryAsync(fqRecordName, QueryType.A);
                var records = result.Answers.ARecords().ToList();
                ClassicAssert.AreEqual(0, records.Count);
            }
        }
        finally
        {
            await _pdnsClient.DeleteZone(zoneId);
        }
    }

    //

    [Test, Explicit]
    public async Task ItShouldCreateAndDeleteAcNameRecord()
    {
        var domainName = $"unit-test.{Guid.NewGuid()}.id.pub";
        var zoneId = domainName + ".";
        var recordName = "my.cname.record";
        var fqRecordName = $"{recordName}.{domainName}";
        
        await _pdnsClient.CreateZone(zoneId, new [] { Ns1, Ns2 }, "sebbarg@gmail.com");
        try
        {
            await _pdnsClient.CreateCnameRecords(zoneId, recordName, "homesweethome.id.pub.");
            
            //
            // Verify CNAME is present using our own primary name server
            //
            {
                var client = new LookupClient(IPAddress.Parse(IpNs1));
                var result = await client.QueryAsync(fqRecordName, QueryType.CNAME);
                var records = result.Answers.CnameRecords().ToList();
                ClassicAssert.AreEqual(1, records.Count);
            }
            
            await _pdnsClient.DeleteCnameRecords(zoneId, recordName);
            
            //
            // Verify CNAME is no longer present using our own primary name server
            //
            {
                var client = new LookupClient(IPAddress.Parse(IpNs1));
                var result = await client.QueryAsync(fqRecordName, QueryType.CNAME);
                var records = result.Answers.CnameRecords().ToList();
                ClassicAssert.AreEqual(0, records.Count);
            }
        }
        finally
        {
            await _pdnsClient.DeleteZone(zoneId);
        }
    }
}

