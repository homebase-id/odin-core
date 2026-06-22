using System;
using System.Linq;

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

    // Attribute type ids — sourced from the canonical registry (BuiltInProfileAttributes) so the GUIDs
    // live in exactly one place and cannot drift from what odin-js writes / the client reads.
    public static readonly Guid Name = BuiltInProfileAttributes.Name;
    public static readonly Guid PhoneNumber = BuiltInProfileAttributes.PhoneNumber;
    public static readonly Guid Email = BuiltInProfileAttributes.Email;
    public static readonly Guid Address = BuiltInProfileAttributes.Address;
    public static readonly Guid Birthday = BuiltInProfileAttributes.Birthday;
    public static readonly Guid Photo = BuiltInProfileAttributes.Photo;

    // Large rich-text attributes carried verbatim into the ext_data payload (not flattened into the
    // 10 KB content blob). Attribute names "Experience" / "Bio".
    public static readonly Guid Experience = BuiltInProfileAttributes.Experience;  // toGuidId("full_bio")
    public static readonly Guid Bio = BuiltInProfileAttributes.Bio;               // toGuidId("short_bio")

    // The "Short bio" attribute — a small (~160 char) plain-string tagline flattened into the content
    // header (NOT ext_data). Its type id is a hardcoded GUID in odin-js (BuiltInAttributes.BioSummary),
    // not a toGuidId. Its data field is also named "short_bio" — but it's a string here, unlike the
    // rich-text "short_bio" array inside the "Bio" attribute above. Match by type id to keep them apart.
    public static readonly Guid ShortBioType = BuiltInProfileAttributes.BioSummary;
    public const string ShortBioField = "short_bio";

    /// <summary>The "Status" attribute — a short current-status string flattened into Content.Status.</summary>
    public static readonly Guid Status = BuiltInProfileAttributes.Status;
    public const string StatusField = "status";

    /// <summary>A single personal link / website attribute (its target URL is flattened into Content.Link).</summary>
    public static readonly Guid Link = BuiltInProfileAttributes.Link;

    /// <summary>
    /// Social + game handle attribute types. Each carries one handle, kept verbatim in
    /// <see cref="ContactContent.Social"/> keyed by the attribute's type id (the chosen GUID keying).
    /// Derived from the registry by category so it can't drift from <see cref="BuiltInProfileAttributes"/>.
    /// </summary>
    public static readonly Guid[] SocialTypes = BuiltInProfileAttributes.All
        .Where(t => t.Category is ProfileAttributeCategory.Social or ProfileAttributeCategory.Game)
        .Select(t => t.Type)
        .ToArray();

    /// <summary>The text attribute types enrichment flattens into the contact content blob.</summary>
    public static readonly Guid[] TextTypes = [Name, PhoneNumber, Email, Address, Birthday, ShortBioType, Status];

    /// <summary>Attribute types stored verbatim (keyed by type id) in the ext_data payload.</summary>
    public static readonly Guid[] ExtDataTypes = [Experience, Bio];

    /// <summary>Everything the enrichment ProfileDrive query pulls in one shot.</summary>
    public static readonly Guid[] QueryTypes = [.. TextTypes, .. ExtDataTypes, .. SocialTypes, Link];

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

    public const string LinkTargetField = "link_target"; // odin-js LinkFields.LinkTarget (the URL)
}
