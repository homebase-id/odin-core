using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Services.Contacts;
using Odin.Services.Profile;

namespace Odin.Services.Tests.Profile;

/// <summary>
/// The fixtures in CardContract/ are written by chat-kmp (CardContractGoldenTest) and are byte-identical
/// copies of the ones checked in beside the chat-kmp and odin-js tests.
/// </summary>
public class ProfileCardContractTests
{
    private const string ContractTypeGuid = "9832dc5dd4ba12dd60acb853e7588f49";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Profile", "CardContract", name));

    private static SetProfileAttributeRequest Read(string name) =>
        OdinSystemSerializer.DeserializeOrThrow<SetProfileAttributeRequest>(Fixture(name));

    private static JsonElement Data(SetProfileAttributeRequest request, string key) => (JsonElement)request.Data[key];

    [Test]
    public void TypeIdIsTheSharedContractGuid()
    {
        Assert.That(BuiltInProfileAttributes.ProfileCard.ToString("N"), Is.EqualTo(ContractTypeGuid));
        var registered = BuiltInProfileAttributes.All.Single(t => t.Key == "profile_card");
        Assert.That(registered.TypeId, Is.EqualTo(ContractTypeGuid));
    }

    [Test]
    public void PublicCardCreateDeserialisesWithTheSameMeaning()
    {
        var request = Read("save-public-card.json");

        Assert.That(request.Type.ToString("N"), Is.EqualTo(ContractTypeGuid));
        Assert.That(request.Id, Is.Null);
        Assert.That(request.ExpectedVersionTag, Is.Null);
        Assert.That(request.Visibility, Is.EqualTo(ProfileAttributeVisibility.Anonymous));
        Assert.That(request.CircleIds, Is.Null.Or.Empty);
        Assert.That(request.Priority, Is.EqualTo(1000));
        Assert.That(request.Data.ContainsKey("label"), Is.False);
        Assert.That(Data(request, "design").GetString(), Is.EqualTo("board"));
        AssertOverrides(Data(request, "overrides"));
    }

    [Test]
    public void PublicCardEditCarriesIdAndVersionTag()
    {
        var request = Read("save-public-card-edit.json");

        Assert.That(request.Id, Is.EqualTo(Guid.Parse("11111111-1111-4111-8111-111111111111")));
        Assert.That(request.ExpectedVersionTag, Is.EqualTo(Guid.Parse("22222222-2222-4222-8222-222222222222")));
        Assert.That(request.Visibility, Is.EqualTo(ProfileAttributeVisibility.Anonymous));
        Assert.That(request.Priority, Is.EqualTo(1000));
        Assert.That(Data(request, "futureKey").GetString(), Is.EqualTo("kept"));
    }

    [Test]
    public void CircleCardDeserialisesWithTheSameMeaning()
    {
        var request = Read("save-circle-card.json");

        Assert.That(request.Type.ToString("N"), Is.EqualTo(ContractTypeGuid));
        Assert.That(request.Visibility, Is.EqualTo(ProfileAttributeVisibility.Connected));
        Assert.That(request.Priority, Is.EqualTo(0));
        Assert.That(Data(request, "design").GetString(), Is.EqualTo("dossier"));
        Assert.That(Data(request, "label").GetString(), Is.EqualTo("Friends"));
        AssertOverrides(Data(request, "overrides"));
    }

    [Test]
    public void StoredContentKeepsPriorityAndDataForTheReader()
    {
        var request = Read("save-circle-card.json");
        var content = new ProfileAttributeContent
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = request.Type.ToString("N"),
            Priority = request.Priority ?? 0,
            Data = request.Data
        };

        using var stored = JsonDocument.Parse(OdinSystemSerializer.Serialize(content));
        var root = stored.RootElement;
        Assert.That(root.GetProperty("type").GetString(), Is.EqualTo(ContractTypeGuid));
        Assert.That(root.GetProperty("priority").GetInt32(), Is.EqualTo(0));
        Assert.That(root.GetProperty("data").GetProperty("design").GetString(), Is.EqualTo("dossier"));
        Assert.That(root.GetProperty("data").GetProperty("label").GetString(), Is.EqualTo("Friends"));
        AssertOverrides(root.GetProperty("data").GetProperty("overrides"));
    }

    private static void AssertOverrides(JsonElement overrides)
    {
        Assert.That(overrides.GetProperty("palette").GetProperty("accent").GetString(), Is.EqualTo("#ABCDEF"));
        Assert.That(overrides.GetProperty("palette").EnumerateObject().Count(), Is.EqualTo(6));
        Assert.That(overrides.GetProperty("type").GetProperty("displayCase").GetString(), Is.EqualTo("upper"));
        Assert.That(overrides.GetProperty("portraits")[0].GetProperty("tilt").GetInt32(), Is.EqualTo(5));
        Assert.That(overrides.GetProperty("portraits")[0].GetProperty("tape").GetBoolean(), Is.True);
        Assert.That(overrides.GetProperty("blocks").GetArrayLength(), Is.EqualTo(2));
        Assert.That(overrides.GetProperty("socials").GetString(), Is.EqualTo("handles"));
    }
}
