using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;

namespace Odin.Services.Tests.Authorization.Apps;

/// <summary>
/// Slugs are immutable wire addresses that other identities resolve against, and the column is
/// UNIQUE per identity -- so the generator has to be right the first time.
/// </summary>
[TestFixture]
public class AppSlugGeneratorTests
{
    [Test]
    public void KnownSystemAppsGetTheirName()
    {
        var slugs = AppSlugGenerator.GenerateAll([
            (SystemAppConstants.ChatAppId, "Homebase - Chat"),
            (SystemAppConstants.FeedAppId, "Homebase - Feed"),
            (SystemAppConstants.PhotoAppId, "Homebase - Photos"),
            (SystemAppConstants.SystemAppId, "Owner")
        ]);

        Assert.That(slugs[SystemAppConstants.ChatAppId], Is.EqualTo("chat"));
        Assert.That(slugs[SystemAppConstants.FeedAppId], Is.EqualTo("feed"));
        Assert.That(slugs[SystemAppConstants.PhotoAppId], Is.EqualTo("photo"));
        Assert.That(slugs[SystemAppConstants.SystemAppId], Is.EqualTo("system"));
    }

    /// <summary>
    /// Every app the tree names is known, not just the handful that used to be listed by hand.
    /// </summary>
    /// <remarks>
    /// Recovery is the case that caught this: it is in the tree as <c>recovery</c>, was not in the
    /// generator's own list, and so was slugged off its display name as <c>homebase-recov</c> -- an
    /// address the v13-&gt;v14 stamp then had to correct.
    /// </remarks>
    [Test]
    public void EveryAppInTheTreeGetsTheSlugTheTreeNames()
    {
        var slugs = AppSlugGenerator.GenerateAll(
            BuiltinApps.All.Select(app => (app.AppId, (string?)app.Name)));

        foreach (var app in BuiltinApps.All)
        {
            Assert.That(slugs[app.AppId], Is.EqualTo(app.AppSlug),
                $"{app.Name} should be addressed as '{app.AppSlug}'");
        }
    }

    /// <summary>
    /// The display name is what a registration actually carries, and it is not the tree's name.
    /// </summary>
    [Test]
    public void ARecoveryAppRegisteredByItsDisplayNameIsStillRecovery()
    {
        var slugs = AppSlugGenerator.GenerateAll([
            (SystemAppConstants.RecoveryAppId, "Homebase - Recovery")
        ]);

        Assert.That(slugs[SystemAppConstants.RecoveryAppId], Is.EqualTo("recovery"));
    }

    [Test]
    public void UnknownAppsAreDerivedFromTheirName()
    {
        var acme = Guid.NewGuid();
        var slugs = AppSlugGenerator.GenerateAll([(acme, "Acme Receipts")]);

        Assert.That(slugs[acme], Is.EqualTo("acme-receipts"));
        Assert.That(AppSlugGenerator.IsValid(slugs[acme]), Is.True);
    }

    [Test]
    public void CollidingNamesAreDisambiguated()
    {
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var third = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var slugs = AppSlugGenerator.GenerateAll([
            (first, "Notes"),
            (second, "Notes"),
            (third, "Notes")
        ]);

        Assert.That(slugs.Values.Distinct().Count(), Is.EqualTo(3), "every app must get a distinct slug");
        Assert.That(slugs.Values, Has.Member("notes"));

        foreach (var slug in slugs.Values)
        {
            Assert.That(AppSlugGenerator.IsValid(slug), Is.True, $"'{slug}' is not a valid slug");
        }
    }

    [Test]
    public void ASystemAppNeverYieldsItsSlugToADerivedOne()
    {
        // An app literally called "Chat" must not take the chat app's address.
        var impostor = Guid.NewGuid();

        var slugs = AppSlugGenerator.GenerateAll([
            (impostor, "Chat"),
            (SystemAppConstants.ChatAppId, "Homebase - Chat")
        ]);

        Assert.That(slugs[SystemAppConstants.ChatAppId], Is.EqualTo("chat"));
        Assert.That(slugs[impostor], Is.Not.EqualTo("chat"));
    }

    [Test]
    public void AnUnslugifiableNameFallsBackToTheAppId()
    {
        var appId = Guid.NewGuid();
        var slugs = AppSlugGenerator.GenerateAll([(appId, "!!! ??? ***")]);

        Assert.That(AppSlugGenerator.IsValid(slugs[appId]), Is.True);
        Assert.That(slugs[appId], Is.EqualTo(appId.ToString("N")[..AppSlugGenerator.MaxLength]));
    }

    [Test]
    public void NullAndEmptyNamesStillGetAValidSlug()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var slugs = AppSlugGenerator.GenerateAll([(a, null), (b, "   ")]);

        Assert.That(AppSlugGenerator.IsValid(slugs[a]), Is.True);
        Assert.That(AppSlugGenerator.IsValid(slugs[b]), Is.True);
        Assert.That(slugs[a], Is.Not.EqualTo(slugs[b]));
    }

    [Test]
    public void GenerateRespectsSlugsAlreadyTaken()
    {
        // The stored slug wins, even where the name would slugify to something else today.
        var appId = Guid.NewGuid();
        var taken = new HashSet<string> { "acme-receipts" };

        var slug = AppSlugGenerator.Generate(appId, "Acme Receipts", taken);

        Assert.That(slug, Is.Not.EqualTo("acme-receipts"));
        Assert.That(AppSlugGenerator.IsValid(slug), Is.True);
    }

    [Test]
    public void DuplicateAppIdsAreRejected()
    {
        var appId = Guid.NewGuid();

        Assert.Throws<OdinSystemException>(() =>
            AppSlugGenerator.GenerateAll([(appId, "One"), (appId, "Two")]));
    }

    [Test]
    [TestCase("Homebase - Chat", "homebase-chat")]
    [TestCase("UPPER case", "upper-case")]
    [TestCase("  leading and trailing  ", "leading-and-tr")]
    [TestCase("multiple---hyphens", "multiple-hyphe")]
    [TestCase("café", "caf")]
    [TestCase("123", "123")]
    [TestCase("!!!", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void SlugifyProducesAValidSegmentOrNothing(string input, string expected)
    {
        var result = AppSlugGenerator.Slugify(input);

        Assert.That(result, Is.EqualTo(expected));

        if (result != null)
        {
            Assert.That(AppSlugGenerator.IsValid(result), Is.True);
            Assert.That(result.Length, Is.LessThanOrEqualTo(AppSlugGenerator.MaxLength));
        }
    }

    [Test]
    public void SlugsNeverExceedTheLengthCap()
    {
        var appId = Guid.NewGuid();
        var slugs = AppSlugGenerator.GenerateAll([
            (appId, "An Extremely Long Application Name That Will Not Fit")
        ]);

        Assert.That(slugs[appId].Length, Is.LessThanOrEqualTo(AppSlugGenerator.MaxLength));
        Assert.That(AppSlugGenerator.IsValid(slugs[appId]), Is.True);
    }
}
