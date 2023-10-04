using System;
using Odin.Core.Services.Drives.Management;

namespace Odin.Core.Services.Drives;

/// <summary>
/// Built-in drives
/// </summary>
public static class SystemDriveConstants
{
    public static readonly Guid ChannelDriveType = Guid.Parse("c32acb69-9d38-4c90-b37a-8755263ded8c");
    
    public static readonly TargetDrive TransientTempDrive = new()
    {
        Alias = GuidId.FromString("transit_temp_drive"),
        Type = GuidId.FromString("transit_temp_drive")
    };

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

    public static readonly TargetDrive WalletDrive = new()
    {
        Alias = GuidId.FromString("standard_wallet_drive"),
        Type = GuidId.FromString("profile_drive")
    };

    public static readonly TargetDrive ChatDrive = new()
    {
        Alias = GuidId.FromString("builtin_chat_drive"),
        Type = GuidId.FromString("chat_drive")
    };

    public static readonly TargetDrive FeedDrive = new()
    {
        Alias = GuidId.FromString("builtin_feed_drive"),
        Type = GuidId.FromString("feed_drive")
    };
    
    
    public static readonly CreateDriveRequest CreateTransientTempDriveRequest = new()
    {
        Name = "Transient temp drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = TransientTempDrive,
        OwnerOnly = false
    };
    
    public static readonly CreateDriveRequest CreateFeedDriveRequest = new()
    {
        Name = "Feed",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = FeedDrive,
        OwnerOnly = true
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

    public static readonly CreateDriveRequest CreateWalletDriveRequest = new()
    {
        Name = "Wallet",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = WalletDrive,
        OwnerOnly = true
    };

    public static readonly CreateDriveRequest CreateChatDriveRequest = new()
    {
        Name = "Chat Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = ChatDrive,
        OwnerOnly = false //TODO: this needs to be set to true but is waiting on decision for how to auto-provision it.  I set it to false so it could be added to the system circle
    };

}
