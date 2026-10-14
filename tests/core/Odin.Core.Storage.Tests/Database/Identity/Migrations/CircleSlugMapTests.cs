using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity.Migrations;
using Odin.Services.Apps.Builtin;
using Odin.Services.Membership.Circles;

namespace Odin.Core.Storage.Tests.Database.Identity.Migrations;

/// <summary>
/// The Circle table migration slugged existing built-in circles from a frozen map; new identities get
/// theirs from <see cref="BuiltinCircles"/>.  The two must agree, or one identity's chat circle is
/// <c>chat</c> and another's something else (#1939).
/// </summary>
public class CircleSlugMapTests
{
    [Test]
    public void TheMigrationsBuiltinSlugsMatchTheLiveDeclarations()
    {
        var live = typeof(BuiltinCircles)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(CircleDefinition))
            .Select(f => (CircleDefinition)f.GetValue(null))
            .ToDictionary(c => c.Id.Value, c => c.Slug);

        // The migration's frozen map also slugs the two system circles, which v19 -> v20 deletes (#1809)
        var retiredSystemCircles = new[]
        {
            Guid.Parse("bb2683fa402aff866e771a6495765a15"),
            Guid.Parse("9e22b42952f74d2580e11250b651d343")
        };
        var frozen = TableCircleMigrationV202610140701.KnownCircleSlugs
            .Where(kv => !retiredSystemCircles.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.That(frozen, Is.EquivalentTo(live));
    }

    [Test]
    public void EveryBuiltinSlugIsValidAndUnique()
    {
        var slugs = CircleSlugGenerator.BuiltinCircleSlugs().ToList();
        Assert.That(slugs, Is.All.Matches<string>(Odin.Services.Base.OdinSlug.IsValid));
        Assert.That(slugs, Is.Unique);
    }
}
