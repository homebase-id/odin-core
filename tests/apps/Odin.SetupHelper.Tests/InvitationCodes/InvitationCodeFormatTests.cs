using Odin.SetupHelper.InvitationCodes;

namespace Odin.SetupHelper.Tests.InvitationCodes;

public class InvitationCodeFormatTests
{
    [Test]
    public void GeneratedCodesAreTwelveCharactersThatNormalizeToThemselves()
    {
        for (var i = 0; i < 100; i++)
        {
            var code = InvitationCodeFormat.Generate();
            Assert.That(code, Has.Length.EqualTo(12));
            Assert.That(InvitationCodeFormat.Normalize(code), Is.EqualTo(code));
        }
    }

    [TestCase("K7QX-M2PD-9FHW", "K7QXM2PD9FHW")]
    [TestCase("k7qx m2pd 9fhw", "K7QXM2PD9FHW")]
    [TestCase("K7QXM2PD9FHW", "K7QXM2PD9FHW")]
    [TestCase("O1IL-0000-0000", "01110000" + "0000")] // Crockford's forgiving letters
    public void NormalizeAcceptsHowPeopleTypeACode(string input, string expected)
    {
        Assert.That(InvitationCodeFormat.Normalize(input), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("K7QX-M2PD")]           // too short
    [TestCase("K7QX-M2PD-9FHW-A")]    // too long
    [TestCase("K7QX-M2PD-9FHU")]      // U is not in the alphabet
    [TestCase("K7QX-M2PD-9FH!")]
    public void NormalizeRejectsWhatCannotBeACode(string? input)
    {
        Assert.That(InvitationCodeFormat.Normalize(input), Is.Null);
    }

    [Test]
    public void DisplayGroupsInFours()
    {
        Assert.That(InvitationCodeFormat.Display("K7QXM2PD9FHW"), Is.EqualTo("K7QX-M2PD-9FHW"));
    }
}
