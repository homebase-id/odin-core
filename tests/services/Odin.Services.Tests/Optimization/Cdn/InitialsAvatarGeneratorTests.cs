using System;
using System.Text;
using NUnit.Framework;
using Odin.Services.Optimization.Cdn;

namespace Odin.Services.Tests.Optimization.Cdn;

[TestFixture]
public class InitialsAvatarGeneratorTests
{
    [Test]
    public void TryGenerate_GivenNameOnly_ProducesSingleInitial()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("frodo", null, "frodo.dotyou.cloud", out var svgBase64);

        Assert.That(ok, Is.True);
        var svg = Decode(svgBase64!);
        Assert.That(svg, Does.Contain(">F<"));
    }

    [Test]
    public void TryGenerate_GivenNameAndSurname_ProducesTwoInitials()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("Frodo", "Baggins", "frodo.dotyou.cloud", out var svgBase64);

        Assert.That(ok, Is.True);
        var svg = Decode(svgBase64!);
        Assert.That(svg, Does.Contain(">FB<"));
    }

    [TestCase(null, null)]
    [TestCase("", "Baggins")]
    [TestCase("   ", "Baggins")]
    public void TryGenerate_NoGivenName_ReturnsFalse(string givenName, string surname)
    {
        var ok = InitialsAvatarGenerator.TryGenerate(givenName, surname, "frodo.dotyou.cloud", out var svgBase64);

        Assert.That(ok, Is.False);
        Assert.That(svgBase64, Is.Null);
    }

    [Test]
    public void TryGenerate_SameSeed_ProducesSameColorAcrossCalls()
    {
        InitialsAvatarGenerator.TryGenerate("Frodo", "Baggins", "frodo.dotyou.cloud", out var first);
        InitialsAvatarGenerator.TryGenerate("Frodo", "Baggins", "frodo.dotyou.cloud", out var second);

        // Guards against accidentally using string.GetHashCode(), which is randomized per-process in
        // .NET -- the same identity must always land on the same avatar color, including across restarts.
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void TryGenerate_UnicodeGivenName_DoesNotThrowAndUppercasesInitial()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("Émile", null, "emile.dotyou.cloud", out var svgBase64);

        Assert.That(ok, Is.True);
        var svg = Decode(svgBase64!);
        Assert.That(svg, Does.Contain(">É<"));
    }

    [Test]
    public void TryGenerate_LeadingEmojiBeforeLetters_SkipsToFirstLetter()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("🎉Frodo", null, "frodo.dotyou.cloud", out var svgBase64);

        Assert.That(ok, Is.True);
        var svg = Decode(svgBase64!);
        Assert.That(svg, Does.Contain(">F<"));
    }

    [Test]
    public void TryGenerate_OnlyEmoji_ReturnsFalse()
    {
        var ok = InitialsAvatarGenerator.TryGenerate("🎉🎉🎉", null, "frodo.dotyou.cloud", out var svgBase64);

        Assert.That(ok, Is.False);
        Assert.That(svgBase64, Is.Null);
    }

    private static string Decode(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
