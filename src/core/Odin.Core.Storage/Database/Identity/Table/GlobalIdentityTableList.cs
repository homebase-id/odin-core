using System;
using System.Collections.Immutable;

namespace Odin.Core.Storage.Database.Identity.Table;

public class GlobalIdentityTableList
{
    public static readonly ImmutableList<Type> TableList = [
            typeof(TableDrives),
            typeof(TableDriveMainIndex),
            typeof(TableDriveTransferHistory),
            typeof(TableDriveAclIndex),
            typeof(TableDriveTagIndex),
            typeof(TableDriveLocalTagIndex),
            typeof(TableDriveReactions),
            typeof(TableAppNotifications),
            typeof(TableCircle),
            typeof(TableCircleMember),
            typeof(TableConnections),
            typeof(TableAppGrants),
            typeof(TableImFollowing),
            typeof(TableFollowsMe),
            typeof(TableInbox),
            typeof(TableOutbox),
            typeof(TableKeyValue),
            typeof(TableKeyTwoValue),
            typeof(TableKeyThreeValue),
            typeof(TableKeyUniqueThreeValue),
            typeof(TableNonce),
    ];

    private Lazy<TableDrives> _drives;
    public TableDrives => LazyResolve(ref _drives);

    private Lazy<TableDriveMainIndex> _driveMainIndex;
    public TableDriveMainIndex => LazyResolve(ref _driveMainIndex);

    private Lazy<TableDriveTransferHistory> _driveTransferHistory;
    public TableDriveTransferHistory => LazyResolve(ref _driveTransferHistory);

    private Lazy<TableDriveAclIndex> _driveAclIndex;
    public TableDriveAclIndex => LazyResolve(ref _driveAclIndex);

    private Lazy<TableDriveTagIndex> _driveTagIndex;
    public TableDriveTagIndex => LazyResolve(ref _driveTagIndex);

    private Lazy<TableDriveLocalTagIndex> _driveLocalTagIndex;
    public TableDriveLocalTagIndex => LazyResolve(ref _driveLocalTagIndex);

    private Lazy<TableDriveReactions> _driveReactions;
    public TableDriveReactions => LazyResolve(ref _driveReactions);

    private Lazy<TableAppNotifications> _appNotifications;
    public TableAppNotifications => LazyResolve(ref _appNotifications);

    private Lazy<TableCircle> _circle;
    public TableCircle => LazyResolve(ref _circle);

    private Lazy<TableCircleMember> _circleMember;
    public TableCircleMember => LazyResolve(ref _circleMember);

    private Lazy<TableConnections> _connections;
    public TableConnections => LazyResolve(ref _connections);

    private Lazy<TableAppGrants> _appGrants;
    public TableAppGrants => LazyResolve(ref _appGrants);

    private Lazy<TableImFollowing> _imFollowing;
    public TableImFollowing => LazyResolve(ref _imFollowing);

    private Lazy<TableFollowsMe> _followsMe;
    public TableFollowsMe => LazyResolve(ref _followsMe);

    private Lazy<TableInbox> _inbox;
    public TableInbox => LazyResolve(ref _inbox);

    private Lazy<TableOutbox> _outbox;
    public TableOutbox => LazyResolve(ref _outbox);

    private Lazy<TableKeyValue> _keyValue;
    public TableKeyValue => LazyResolve(ref _keyValue);

    private Lazy<TableKeyTwoValue> _keyTwoValue;
    public TableKeyTwoValue => LazyResolve(ref _keyTwoValue);

    private Lazy<TableKeyThreeValue> _keyThreeValue;
    public TableKeyThreeValue => LazyResolve(ref _keyThreeValue);

    private Lazy<TableKeyUniqueThreeValue> _keyUniqueThreeValue;
    public TableKeyUniqueThreeValue => LazyResolve(ref _keyUniqueThreeValue);

    private Lazy<TableNonce> _nonce;
    public TableNonce => LazyResolve(ref _nonce);

}
