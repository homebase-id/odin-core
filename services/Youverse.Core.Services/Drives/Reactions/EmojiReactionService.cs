using System.Collections.Generic;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Drives.Reactions;

//TODO: need to determine if I want to validate if the file exists.  file exist calls are expensive 

/// <summary>
/// Manages emoji reactions to files
/// </summary>
public class EmojiReactionService
{
    private readonly DotYouContextAccessor _contextAccessor;
    private readonly DriveDatabaseHost _driveDatabaseHost;

    public EmojiReactionService(DriveDatabaseHost driveDatabaseHost, DotYouContextAccessor contextAccessor)
    {
        _driveDatabaseHost = driveDatabaseHost;
        _contextAccessor = contextAccessor;
    }

    public void AddReaction(OdinId dotYouId, InternalDriveFileId file, string reactionContent)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.WriteReactionsAndComments);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            manager.AddReaction(dotYouId, file.FileId, reactionContent);
        }
    }

    public void DeleteReaction(OdinId dotYouId, InternalDriveFileId file, string reactionContent)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.WriteReactionsAndComments);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            manager.DeleteReaction(dotYouId, file.FileId, reactionContent);
        }
    }

    public void DeleteReactions(OdinId dotYouId, InternalDriveFileId file)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.WriteReactionsAndComments);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            manager.DeleteReactions(dotYouId, file.FileId);
        }
    }

    public GetReactionsResponse GetReactions(InternalDriveFileId file)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            var (list, count) = manager.GetReactions(file.FileId);

            return new GetReactionsResponse()
            {
                Reactions = list,
                TotalCount = count
            };
        }

        throw new YouverseSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }

    public GetReactionsResponse2 GetReactions2(InternalDriveFileId file, int cursor, int maxCount)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasDrivePermission(file.DriveId, DrivePermission.Read);

        if (_driveDatabaseHost.TryGetOrLoadQueryManager(file.DriveId, out var manager))
        {
            var (list, nextCursor) = manager.GetReactionsByFile(fileId: file.FileId, cursor: cursor, maxCount: maxCount);

            return new GetReactionsResponse2()
            {
                Reactions = list,
                Cursor = nextCursor
            };
        }

        throw new YouverseSystemException($"Invalid query manager instance for drive {file.DriveId}");
    }
}