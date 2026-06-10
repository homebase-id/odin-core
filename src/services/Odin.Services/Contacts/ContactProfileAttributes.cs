using System;

namespace Odin.Services.Contacts;

/// <summary>
/// Profile attribute type ids and field keys used when enriching a contact from an identity's
/// <c>ProfileDrive</c>. Ported from odin-js (`BuiltInAttributes` / `*Fields` in
/// `profile/ProfileData/ProfileConfig.ts`); attribute files are tagged with these type ids
/// (`tagsMatchAtLeastOne`) and carry a <see cref="Odin.Services.LinkPreview.Profile.ProfileBlock"/>
/// whose <c>Data</c> holds the field values below.
/// </summary>
internal static class ContactProfileAttributes
{
    // Attribute type ids (md5 of the type name), matching odin-js BuiltInAttributes.
    public static readonly Guid Name = ContactGuid.ToGuidId("name");
    public static readonly Guid PhoneNumber = ContactGuid.ToGuidId("phonenumber");
    public static readonly Guid Email = ContactGuid.ToGuidId("email");
    public static readonly Guid Address = ContactGuid.ToGuidId("location");
    public static readonly Guid Birthday = ContactGuid.ToGuidId("birthday");
    public static readonly Guid Photo = ContactGuid.ToGuidId("photo");

    /// <summary>The text attribute types enrichment pulls today (image/photo handled separately, later).</summary>
    public static readonly Guid[] TextTypes = [Name, PhoneNumber, Email, Address, Birthday];

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
