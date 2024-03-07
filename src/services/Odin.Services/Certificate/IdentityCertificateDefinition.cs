using System;
using System.Collections.Generic;
using System.Linq;

namespace Odin.Services.Certificate;

/// <summary>
/// Defines the domains to be setup for a given certificate
/// </summary>
public class IdentityCertificateDefinition
{
    public string Domain { get; set; }
    public List<string> AlternativeNames { get; set; }

    /// <summary>
    /// Returns true if this definition has the domain as primary or an alternative domain
    /// </summary>
    public bool HasDomain(string domain)
    {
        return Domain.Equals(domain, StringComparison.InvariantCultureIgnoreCase)
               || AlternativeNames.Any(d => d.Equals(domain, StringComparison.InvariantCultureIgnoreCase));
    }
}