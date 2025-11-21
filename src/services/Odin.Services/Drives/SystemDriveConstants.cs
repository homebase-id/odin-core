using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Services.Drives.Management;

namespace Odin.Services.Drives;

/// <summary>
/// Built-in drives
/// </summary>
public static class SystemDriveConstants
{
    //
    // DO NOT CHANGE ANY VALUES
    //

    public static readonly Guid ChannelDriveType = Guid.Parse("8f448716-e34c-edf9-0141-45e043ca6612");
    
    public static readonly TargetDrive ShardRecoveryDrive = new()
    {
        Alias = Guid.Parse("46242d0d67604b2aa683f05cd48d4aef"),
        Type = Guid.Parse("43138ae90206480b9ff493580ca147ee")
    };
    
    public static readonly TargetDrive TransientTempDrive = new()
    {
        Alias = Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c"),
        Type = Guid.Parse("90f5e74ab7f9efda0ac298373a32ad8c"),
    };

    public static readonly TargetDrive ContactDrive = new()
    {
        Alias = Guid.Parse("2612429d1c3f037282b8d42fb2cc0499"),
        Type = Guid.Parse("70e92f0f94d05f5c7dcd36466094f3a5")
    };

    public static readonly TargetDrive ProfileDrive = new()
    {
        Alias = Guid.Parse("8f12d8c4933813d378488d91ed23b64c"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    public static readonly TargetDrive WalletDrive = new()
    {
        Alias = Guid.Parse("a6f991e214b11c8c9796f664e1ec0cac"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    public static readonly TargetDrive ChatDrive = new()
    {
        Alias = Guid.Parse("9ff813aff2d61e2f9b9db189e72d1a11"),
        Type = Guid.Parse("66ea8355ae4155c39b5a719166b510e3")
    };

    public static readonly TargetDrive MailDrive = new()
    {
        Alias = Guid.Parse("e69b5a48a663482fbfd846f3b0b143b0"),
        Type = Guid.Parse("2dfecc40311e41e5a12455e925144202")
    };

    public static readonly TargetDrive FeedDrive = new()
    {
        Alias = Guid.Parse("4db49422ebad02e99ab96e9c477d1e08"),
        Type = Guid.Parse("a3227ffba87608beeb24fee9b70d92a6")
    };

    public static readonly TargetDrive HomePageConfigDrive = new()
    {
        Alias = Guid.Parse("ec83345af6a747d4404ef8b0f8844caa"),
        Type = Guid.Parse("597241530e3ef24b28b9a75ec3a5c45c")
    };

    public static readonly TargetDrive PublicPostsChannelDrive = new()
    {
        Alias = Guid.Parse("e8475dc46cb4b6651c2d0dbd0f3aad5f"),
        Type = ChannelDriveType
    };

    public static readonly List<TargetDrive> SystemDrives =
    [
        TransientTempDrive,
        ContactDrive,
        ProfileDrive,
        WalletDrive,
        ChatDrive,
        FeedDrive,
        HomePageConfigDrive,
        MailDrive,
        PublicPostsChannelDrive,
        ShardRecoveryDrive
    ];
    
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

    public static readonly CreateDriveRequest CreateHomePageConfigDriveRequest = new()
    {
        Name = "Homepage Config",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = HomePageConfigDrive,
        OwnerOnly = false
    };

    public static readonly CreateDriveRequest CreatePublicPostsChannelDriveRequest = new()
    {
        Name = "Public Posts",
        AllowAnonymousReads = true,
        Metadata = "",
        TargetDrive = PublicPostsChannelDrive,
        OwnerOnly = false,
        AllowSubscriptions = true
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

    public static readonly CreateDriveRequest CreateShardRecoveryDriveRequest = new()
    {
        Name = "Shard Recovery Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = ShardRecoveryDrive,
        OwnerOnly = false
    };

    
    public static readonly CreateDriveRequest CreateMailDriveRequest = new()
    {
        Name = "Mail Drive",
        AllowAnonymousReads = false,
        Metadata = "",
        TargetDrive = MailDrive,
        OwnerOnly = false //TODO: this needs to be set to true but is waiting on decision for how to auto-provision it.  I set it to false so it could be added to the system circle
    };
    
    public static bool IsSystemDrive(Guid driveId)
    {
        return SystemDrives.Any(d => d.Alias == driveId);
    }
}
