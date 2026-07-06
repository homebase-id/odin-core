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
    public const string ShortBioField = ProfileAttributeFields.ShortBio;

    /// <summary>The "Status" attribute — a short current-status string flattened into Content.Status.</summary>
    public static readonly Guid Status = BuiltInProfileAttributes.Status;
    public const string StatusField = ProfileAttributeFields.Status;

    /// <summary>The "Nickname" attribute — a preferred name flattened into Content.Nickname.</summary>
    public static readonly Guid Nickname = BuiltInProfileAttributes.Nickname;
    public const string NicknameField = ProfileAttributeFields.Nickname;

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
    public static readonly Guid[] TextTypes = [Name, PhoneNumber, Email, Address, Birthday, ShortBioType, Status, Nickname];

    /// <summary>Attribute types stored verbatim (keyed by type id) in the ext_data payload.</summary>
    public static readonly Guid[] ExtDataTypes = [Experience, Bio];

    /// <summary>Everything the enrichment ProfileDrive query pulls in one shot.</summary>
    public static readonly Guid[] QueryTypes = [.. TextTypes, .. ExtDataTypes, .. SocialTypes, Link];

    // Field keys within an attribute's Data dictionary — aliases to the shared source of truth
    // (ProfileAttributeFields), so the reader here and the writer (ProfileAttributeService) cannot drift.
    public const string DisplayName = ProfileAttributeFields.DisplayName;
    public const string GivenName = ProfileAttributeFields.GivenName;
    public const string AdditionalName = ProfileAttributeFields.AdditionalName;
    public const string Surname = ProfileAttributeFields.Surname;

    public const string Label = ProfileAttributeFields.Label;

    public const string AddressLine1 = ProfileAttributeFields.AddressLine1;
    public const string AddressLine2 = ProfileAttributeFields.AddressLine2;
    public const string Postcode = ProfileAttributeFields.Postcode;
    public const string City = ProfileAttributeFields.City;
    public const string Country = ProfileAttributeFields.Country;

    public const string PhoneNumberField = ProfileAttributeFields.PhoneNumber;
    public const string EmailField = ProfileAttributeFields.Email;
    public const string BirthdayDate = ProfileAttributeFields.BirthdayDate;

    public const string LinkTargetField = ProfileAttributeFields.LinkTarget;
}
