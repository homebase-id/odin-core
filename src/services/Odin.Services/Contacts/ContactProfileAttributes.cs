using System;

namespace Odin.Services.Contacts;

/// <summary>
/// Profile attribute type ids and field keys used when enriching a contact from an identity's
/// <c>ProfileDrive</c>. Ported from odin-js (`BuiltInAttributes` / `*Fields` in
/// `profile/ProfileData/ProfileConfig.ts`); attribute files are tagged with these type ids
/// (`tagsMatchAtLeastOne`) and carry a JSON profile attribute whose <c>data</c> object holds the
/// field values below.
/// </summary>
internal static class ContactProfileAttributes
{
    /// <summary>
    /// File type of a profile attribute on the ProfileDrive. Owned here (rather than referenced from
    /// the profile/SSR namespace) so the Contacts code does not depend on that service.
    /// </summary>
    public const int AttributeFileType = 77;

    // Attribute type ids — literal GUIDs matching odin-js BuiltInAttributes. Each is the GUID odin-js
    // tags the attribute file with (and writes into the attribute's `type`), i.e. md5(<source string>).
    public static readonly Guid Name = new("b068931c-c450-442b-63f5-b3d276ea4297");        // md5("name")
    public static readonly Guid PhoneNumber = new("c5754f96-3780-6a28-30ca-2a957c2ac198"); // md5("phonenumber")
    public static readonly Guid Email = new("0c83f57c-786a-0b4a-39ef-ab23731c7ebc");       // md5("email")
    public static readonly Guid Address = new("d5189de0-2792-2f81-0059-51e6efe0efd5");     // md5("location")
    public static readonly Guid Birthday = new("cf673f7e-e888-28c9-fb8f-6acf2cb08403");    // md5("birthday")
    public static readonly Guid Photo = new("5ae0c1c8-a526-0bc7-b664-8f6fbd115c35");       // md5("photo")

    // Large rich-text attributes carried verbatim into the ext_data payload (not flattened into the
    // 10 KB content blob). Attribute names "Experience" / "Bio".
    public static readonly Guid Experience = new("65635623-682c-2fad-d276-7d424f53690f");  // md5("full_bio"),  attribute "Experience"
    public static readonly Guid Bio = new("2cd30a58-568d-c333-2379-44481aeb9ff1");         // md5("short_bio"), attribute "Bio"

    // The "Short bio" attribute — a small (~160 char) plain-string tagline flattened into the content
    // header (NOT ext_data). Its type id is a hardcoded GUID in odin-js (BuiltInAttributes.BioSummary),
    // not an md5. Its data field is also named "short_bio" — but it's a string here, unlike the rich-text
    // "short_bio" array inside the "Bio" attribute above. Match by type id to keep them apart.
    public static readonly Guid ShortBioType = new("1d89f51a-6e42-4074-8d6b-60916c0eec9a");
    public const string ShortBioField = "short_bio";

    /// <summary>The text attribute types enrichment flattens into the contact content blob.</summary>
    public static readonly Guid[] TextTypes = [Name, PhoneNumber, Email, Address, Birthday, ShortBioType];

    /// <summary>Attribute types stored verbatim (keyed by type id) in the ext_data payload.</summary>
    public static readonly Guid[] ExtDataTypes = [Experience, Bio];

    /// <summary>Everything the enrichment ProfileDrive query pulls in one shot.</summary>
    public static readonly Guid[] QueryTypes = [.. TextTypes, .. ExtDataTypes];

    // Field keys within an attribute's Data dictionary (odin-js *Fields).
    public const string DisplayName = "displayName";
    public const string GivenName = "givenName";
    public const string AdditionalName = "additionalName";
    public const string Surname = "surname";

    public const string City = "city";
    public const string Country = "country";

    public const string PhoneNumberField = "phone_number";
    public const string EmailField = "email";
    public const string BirthdayDate = "birtday_date"; // (sic) matches odin-js BirthdayFields.Date
}
