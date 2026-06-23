using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Odin.Services.Contacts;

/// <summary>
/// Canonical, server-side registry of the built-in profile attribute <b>type ids</b> — the GUIDs that
/// odin-js writes into profile attributes and into a contact's <see cref="ContactContent.Social"/> map.
/// These are <b>literals that match the data</b>: each equals odin-js
/// <c>BuiltInAttributes.* = toGuidId(&lt;source string&gt;) = md5(&lt;source string&gt;)</c>, captured here as a
/// constant so nothing — server or client — recomputes an md5 at runtime. Clients fetch this list (see
/// the contacts <c>attribute-types</c> endpoint) and treat each <see cref="ProfileAttributeType.TypeId"/>
/// as an opaque GUID; never derive it.
/// <para>
/// Source of truth: odin-js <c>packages/libs/js-lib/src/profile/ProfileData/ProfileConfig.ts</c>
/// (<c>BuiltInAttributes</c>). Keep in sync if that list changes.
/// </para>
/// </summary>
public static class BuiltInProfileAttributes
{
    // -- Personal ------------------------------------------------------------------------------------
    public static readonly Guid Name = new("b068931c-c450-442b-63f5-b3d276ea4297");          // toGuidId("name")
    public static readonly Guid Nickname = new("e8067417-0aae-0390-9a55-625e9cc9cf97");      // toGuidId("nickname")
    public static readonly Guid Photo = new("5ae0c1c8-a526-0bc7-b664-8f6fbd115c35");         // toGuidId("photo")
    public static readonly Guid Address = new("d5189de0-2792-2f81-0059-51e6efe0efd5");       // toGuidId("location")
    public static readonly Guid Birthday = new("cf673f7e-e888-28c9-fb8f-6acf2cb08403");      // toGuidId("birthday")
    public static readonly Guid PhoneNumber = new("c5754f96-3780-6a28-30ca-2a957c2ac198");   // toGuidId("phonenumber")
    public static readonly Guid Email = new("0c83f57c-786a-0b4a-39ef-ab23731c7ebc");         // toGuidId("email")
    public static readonly Guid Status = new("9acb4454-9b41-5636-97bb-490144ec6258");        // toGuidId("status")

    // -- Social --------------------------------------------------------------------------------------
    public static readonly Guid HomebaseIdentity = new("0eb220c0-9268-57bd-3e31-4a0b9374e1ff"); // toGuidId("dot_you_identity")
    public static readonly Guid Twitter = new("54ecbdc0-35fd-1a44-d052-4303cd104411");       // toGuidId("twitter_username")
    public static readonly Guid Facebook = new("ccda59a7-03e9-4acc-daab-95b58f7c20b6");      // toGuidId("facebook_username")
    public static readonly Guid Instagram = new("345fef7b-ada5-b100-001e-4c78111c86de");     // toGuidId("instagram_username")
    public static readonly Guid Tiktok = new("d58890b2-f156-a0b9-413b-388773b1b0a7");        // toGuidId("tiktok_username")
    public static readonly Guid LinkedIn = new("a050c5ee-4b51-39b7-30cd-7eb44e7db69a");      // toGuidId("linkedin_username")
    public static readonly Guid Youtube = new("90de1008-ca7d-a7a6-272b-2a3235c66989");       // toGuidId("youtube_username")
    public static readonly Guid Discord = new("967c88ca-98b3-50eb-126c-199dd28f49cb");       // toGuidId("discord_username")
    public static readonly Guid Snapchat = new("6d65f3ba-48fc-06ff-edce-f170133577f0");      // toGuidId("snapchat_username")
    public static readonly Guid Github = new("9f1ea770-fb88-720c-4886-1df0f277fcea");        // toGuidId("github_username")
    public static readonly Guid StackOverflow = new("6b801187-7a10-443d-0d41-2dcfad398d06"); // toGuidId("stackoverflow_username")

    // -- Games ---------------------------------------------------------------------------------------
    public static readonly Guid Epic = new("138ce2df-9047-e6f0-4080-a0c870de5bac");          // toGuidId("epic_username")
    public static readonly Guid Riot = new("5c603ef7-e053-d069-10f8-c74618e7ab43");          // toGuidId("riot_username")
    public static readonly Guid Steam = new("e4f27af4-a80d-ff11-caac-432e4c97d79a");         // toGuidId("steam_username")
    public static readonly Guid Minecraft = new("f37a742d-738e-ea92-3a7c-793dc72f8064");     // toGuidId("minecraft_username")

    // -- Link / bio / financial ----------------------------------------------------------------------
    public static readonly Guid Link = new("2a304a13-4845-6ccd-2234-cd71a81bd338");          // toGuidId("link")
    public static readonly Guid Experience = new("65635623-682c-2fad-d276-7d424f53690f");    // toGuidId("full_bio")  — attribute "Experience"
    public static readonly Guid Bio = new("2cd30a58-568d-c333-2379-44481aeb9ff1");           // toGuidId("short_bio") — attribute "Bio" (odin-js BuiltInAttributes.FullBio)
    public static readonly Guid BioSummary = new("1d89f51a-6e42-4074-8d6b-60916c0eec9a");    // hardcoded in odin-js (NOT toGuidId) — "Short bio"
    public static readonly Guid CreditCard = new("3c92742f-3c13-49e9-c46f-e4dd5da62a98");    // toGuidId("creditcard")

    /// <summary>
    /// The full registry, in the client-facing shape served by the contacts <c>attribute-types</c>
    /// endpoint. <see cref="ProfileAttributeType.Key"/> is the stable odin-js source string;
    /// <see cref="ProfileAttributeType.TypeId"/> is the literal GUID the client matches against.
    /// </summary>
    public static readonly IReadOnlyList<ProfileAttributeType> All =
    [
        new("name", Name, ProfileAttributeCategory.Personal),
        new("nickname", Nickname, ProfileAttributeCategory.Personal),
        new("photo", Photo, ProfileAttributeCategory.Personal),
        new("location", Address, ProfileAttributeCategory.Personal),
        new("birthday", Birthday, ProfileAttributeCategory.Personal),
        new("phonenumber", PhoneNumber, ProfileAttributeCategory.Personal),
        new("email", Email, ProfileAttributeCategory.Personal),
        new("status", Status, ProfileAttributeCategory.Personal),

        new("dot_you_identity", HomebaseIdentity, ProfileAttributeCategory.Social),
        new("twitter_username", Twitter, ProfileAttributeCategory.Social),
        new("facebook_username", Facebook, ProfileAttributeCategory.Social),
        new("instagram_username", Instagram, ProfileAttributeCategory.Social),
        new("tiktok_username", Tiktok, ProfileAttributeCategory.Social),
        new("linkedin_username", LinkedIn, ProfileAttributeCategory.Social),
        new("youtube_username", Youtube, ProfileAttributeCategory.Social),
        new("discord_username", Discord, ProfileAttributeCategory.Social),
        new("snapchat_username", Snapchat, ProfileAttributeCategory.Social),
        new("github_username", Github, ProfileAttributeCategory.Social),
        new("stackoverflow_username", StackOverflow, ProfileAttributeCategory.Social),

        new("epic_username", Epic, ProfileAttributeCategory.Game),
        new("riot_username", Riot, ProfileAttributeCategory.Game),
        new("steam_username", Steam, ProfileAttributeCategory.Game),
        new("minecraft_username", Minecraft, ProfileAttributeCategory.Game),

        new("link", Link, ProfileAttributeCategory.Link),
        new("full_bio", Experience, ProfileAttributeCategory.Bio),
        new("short_bio", Bio, ProfileAttributeCategory.Bio),
        new("bio_summary", BioSummary, ProfileAttributeCategory.Bio),
        new("creditcard", CreditCard, ProfileAttributeCategory.Financial),
    ];
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProfileAttributeCategory
{
    Personal,
    Social,
    Game,
    Bio,
    Link,
    Financial
}

/// <summary>
/// One built-in profile attribute type, as handed to clients: a stable <see cref="Key"/> (the odin-js
/// source string) and the literal <see cref="TypeId"/> GUID found in the data. Clients treat
/// <see cref="TypeId"/> as opaque.
/// </summary>
public sealed class ProfileAttributeType(string key, Guid typeId, ProfileAttributeCategory category)
{
    /// <summary>The type id as a GUID, for server-side use.</summary>
    [JsonIgnore]
    public Guid Type { get; } = typeId;

    [JsonPropertyName("key")]
    public string Key { get; } = key;

    /// <summary>
    /// The literal type id <b>exactly as it appears in the data</b>: 32-char lowercase hex, no dashes
    /// (the <c>toGuidId</c> form, e.g. <c>d5189de027922f81005951e6efe0efd5</c>). Clients match this
    /// verbatim against a profile attribute's <c>type</c> / a contact's <c>social</c> keys.
    /// </summary>
    [JsonPropertyName("typeId")]
    public string TypeId { get; } = typeId.ToString("N");

    [JsonPropertyName("category")]
    public ProfileAttributeCategory Category { get; } = category;
}
