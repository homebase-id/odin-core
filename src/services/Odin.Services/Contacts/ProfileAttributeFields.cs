namespace Odin.Services.Contacts;

/// <summary>
/// Canonical field-key names inside a built-in profile attribute's <c>data</c> object — the odin-js
/// <c>*Fields</c> classes in <c>profile/ProfileData/ProfileConfig.ts</c>. This is the single source of
/// truth shared by the <b>reader</b> (<see cref="ContactProfileAttributes"/> /
/// <see cref="ContactEnrichmentService"/>, which parses a peer's ProfileDrive attributes) and the
/// <b>writer</b> (<c>Odin.Services.Profile.ProfileAttributeService</c>, which authors them), so the literal
/// strings live in exactly one place and the two sides cannot drift.
/// <para>Keep in sync with odin-js if those field names change.</para>
/// </summary>
internal static class ProfileAttributeFields
{
    // Name (odin-js MinimalProfileFields)
    public const string DisplayName = "displayName";
    public const string ExplicitDisplayName = "explicitDisplayName";
    public const string GivenName = "givenName";
    public const string AdditionalName = "additionalName";
    public const string Surname = "surname";

    // Shared optional label across Address/Phone/Email attributes (odin-js *Fields.Label), e.g. "Home"/"Work".
    public const string Label = "label";

    // Address (odin-js LocationFields / AddressFields)
    public const string AddressLine1 = "address1";
    public const string AddressLine2 = "address2";
    public const string Postcode = "postcode";
    public const string City = "city";
    public const string Country = "country";

    public const string PhoneNumber = "phone_number";  // odin-js PhoneFields.PhoneNumber
    public const string Email = "email";               // odin-js EmailFields.Email
    public const string BirthdayDate = "birtday_date"; // (sic) odin-js BirthdayFields.Date
    public const string LinkTarget = "link_target";    // odin-js LinkFields.LinkTarget (the URL)
    public const string ShortBio = "short_bio";        // odin-js — the "Short bio" tagline
    public const string Status = "status";             // odin-js MinimalProfileFields.Status
    public const string Nickname = "nickName";         // odin-js NicknameFields.NickName

    // Photo (odin-js MinimalProfileFields.ProfileImageKey) — holds the payload key pointing at the image,
    // not the image itself (set server-side by ProfileAttributeService.SetPhotoAttributeAsync).
    public const string ProfileImageKey = "profileImageKey";
}
