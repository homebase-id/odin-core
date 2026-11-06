using Odin.Services.Drives;

namespace Odin.Services.Tests.Drives;

/// <summary>Drives as the unit tests need them: a <see cref="StorageDrive"/> with only its sharing model set.</summary>
internal static class TestDrives
{
    public static StorageDrive Of(DriveSharingModel model) => new(null, new StorageDriveData { SharingModel = model });
}
