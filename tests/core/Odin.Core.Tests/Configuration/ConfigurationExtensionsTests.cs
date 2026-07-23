using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Odin.Core.Configuration;

namespace Odin.Core.Tests.Configuration;

public class ConfigurationExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values!).Build();
    }

    [Test]
    public void GetOrDefaultShouldReturnDefaultWhenSectionIsMissing()
    {
        var config = BuildConfig([]);
        var result = config.GetOrDefault("Registry:Codes", new List<string> { "fallback" });
        Assert.That(result, Is.EqualTo(new List<string> { "fallback" }));
    }

    [Test]
    public void GetOrDefaultShouldReturnConfiguredValues()
    {
        var config = BuildConfig(new Dictionary<string, string> { ["Registry:Codes:0"] = "abc" });
        var result = config.GetOrDefault("Registry:Codes", new List<string>());
        Assert.That(result, Is.EqualTo(new List<string> { "abc" }));
    }

    [Test]
    public void GetOrDefaultShouldReturnDefaultWhenSectionExistsButBindsToNull()
    {
        // An empty JSON array ("Codes": []) surfaces as an existing section with an empty
        // string value on newer Microsoft.Extensions.Configuration versions; Get<List<string>>()
        // then returns null. GetOrDefault must fall back to the default instead of returning null.
        var config = BuildConfig(new Dictionary<string, string> { ["Registry:Codes"] = "" });
        var result = config.GetOrDefault("Registry:Codes", new List<string>());
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }
}
