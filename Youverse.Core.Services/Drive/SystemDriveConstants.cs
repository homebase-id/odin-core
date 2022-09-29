namespace Youverse.Core.Services.Drive;

/// <summary>
/// Built-in drives
/// </summary>
public static class SystemDriveConstants
{
    public static readonly TargetDrive ContactDrive = new()
    {
        Alias = GuidId.FromString("built_in_contacts_drive"),
        Type = GuidId.FromString("contact_drive")
    };

    public static readonly TargetDrive ProfileDrive = new()
    {
        Alias = GuidId.FromString("standard_profile_drive"),
        Type = GuidId.FromString("profile_drive")
    };

    public static readonly CreateDriveRequest CreateContactDriveRequest = new()
    {
        Name = "Contacts",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = ContactDrive,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateProfileDriveRequest = new()
    {
        Name = "Standard Profile Info",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = ProfileDrive,
        OwnerOnly = false
    };
}