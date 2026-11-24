namespace Odin.Services.Drives;

/// <summary>
/// What a soft delete's tombstone keeps beyond the default, e.g. <c>{ "keepUniqueId": true }</c>. Every field defaults
/// to today's tombstone, so leaving this out, or any field of it, changes nothing; each field is an opt-in that an app
/// asks for. Applies to the deleting identity's own copy only: recipients' copies get the default tombstone.
/// </summary>
public class TombstoneOptions
{
    /// <summary>
    /// False by default: the tombstone has no unique id. When true it keeps the file's unique id, so a query by that id
    /// with fileState Deleted still finds it, and the id stays taken: a new file with it is refused with 4105
    /// ExistingFileWithUniqueId until the tombstone is hard deleted.
    /// </summary>
    public bool KeepUniqueId { get; init; }
}
