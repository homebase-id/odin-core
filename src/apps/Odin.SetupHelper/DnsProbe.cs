using DnsClient;
using Odin.Core.Cache;
using Odin.Core.Dns;
using Odin.Core.Util;

namespace Odin.SetupHelper;

public class DnsProbe(IGenericMemoryCache cache, IAuthoritativeDnsLookup authoritativeDnsLookup, ILookupClient lookupClient)
{
    public record ResolveIpResult(string Ip, string Message);
    public async Task<ResolveIpResult> ResolveIpAsync(string domainName, CancellationToken cancellationToken = default)
    {
        domainName = domainName.ToLower();
        if (!AsciiDomainNameValidator.TryValidateDomain(domainName))
        {
            return new ResolveIpResult("", "Invalid domain name");
        }
    
        var cacheKey = $"resolve-ip:{domainName}";
        if (cache.TryGet<ResolveIpResult>(cacheKey, out var result) && result != null)
        {
            return result with { Message = $"{result.Message} [cache hit]" };
        }
    
        var authorityLookup = await LookupDomainAuthority(domainName, cancellationToken);
        if (authorityLookup.Authority == "")
        {
            return new ResolveIpResult("", authorityLookup.Message);
        }
        
        var resolvers = new List<string> { authorityLookup.Authority };
   
        var options = new DnsQueryOptions
        {
            Recursion = false,
            UseCache = false,
        };
        
        var queryResponse = await lookupClient.Query(resolvers, domainName, QueryType.A, options, cancellationToken: cancellationToken);
        var aRecord = queryResponse?.Answers.ARecords().FirstOrDefault();
        if (aRecord != null)
        {
            result = new ResolveIpResult(aRecord.Address.ToString(), $"Resolved {domainName} to {aRecord.Address}");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
        
        var cNameRecord = queryResponse?.Answers.CnameRecords().FirstOrDefault();
        while (cNameRecord != null)
        {
            authorityLookup = await LookupDomainAuthority(cNameRecord.CanonicalName, cancellationToken);
            if (authorityLookup.Authority == "")
            {
                return new ResolveIpResult("", authorityLookup.Message);
            }

            resolvers = [authorityLookup.Authority];
            
            queryResponse = await lookupClient.Query(resolvers, cNameRecord.CanonicalName, QueryType.A, options, cancellationToken: cancellationToken);
            aRecord = queryResponse?.Answers.ARecords().FirstOrDefault(); // SEB:TODO handle multiple A records
            if (aRecord != null)
            {
                result = new ResolveIpResult(aRecord.Address.ToString(), $"Resolved {domainName} to {aRecord.Address}");
                cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
                return result;
            }
    
            cNameRecord = queryResponse?.Answers.CnameRecords().FirstOrDefault();
        }
        
        result = new ResolveIpResult("", $"Did not find neither A nor CNAME record for {domainName} at authoritative name servers");
        cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
        return result;
    }
    
    //
    
    public record AuthorityResult(string Authority, string Message);
    public async Task<AuthorityResult> LookupDomainAuthority(string domainName, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"domain-authority:{domainName}";
        if (cache.TryGet<AuthorityResult>(cacheKey, out var result) && result != null)
        {
            return result with { Message = $"{result.Message} [cache hit]" };
        }
    
        var authoritativeResult = await authoritativeDnsLookup.LookupDomainAuthorityAsync(domainName, cancellationToken);
        if (authoritativeResult.Exception != null)
        {
            result = new AuthorityResult("", authoritativeResult.Exception.Message);  
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }

        if (authoritativeResult.AuthoritativeNameServer == "")
        {
            result = new AuthorityResult("", $"No authoritative name server found for {domainName}");
            cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
            return result;
        }
    
        result = new AuthorityResult(authoritativeResult.AuthoritativeNameServer, $"Authoritative name server found for {domainName}");
        cache.Set(cacheKey, result, Expiration.Relative(TimeSpan.FromSeconds(5)));
        return result;
    }
    
}