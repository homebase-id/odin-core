using System;
using System.Collections.Immutable;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;

namespace Odin.Core.Storage.Database.Identity;

public partial class IdentityDatabase
{
    public static readonly ImmutableList<Type> TableTypes = [
            typeof(TableDrives),
            typeof(TableDriveMainIndex),
            typeof(TableDriveTransferHistory),
            typeof(TableDriveAclIndex),
            typeof(TableDriveTagIndex),
            typeof(TableDriveLocalTagIndex),
            typeof(TableDriveReactions),
            typeof(TableAppNotifications),
            typeof(TableCats),
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
    public TableDrives Drives => LazyResolve(ref _drives);

    private Lazy<TableDriveMainIndex> _driveMainIndex;
    public TableDriveMainIndex DriveMainIndex => LazyResolve(ref _driveMainIndex);

    private Lazy<TableDriveTransferHistory> _driveTransferHistory;
    public TableDriveTransferHistory DriveTransferHistory => LazyResolve(ref _driveTransferHistory);

    private Lazy<TableDriveAclIndex> _driveAclIndex;
    public TableDriveAclIndex DriveAclIndex => LazyResolve(ref _driveAclIndex);

    private Lazy<TableDriveTagIndex> _driveTagIndex;
    public TableDriveTagIndex DriveTagIndex => LazyResolve(ref _driveTagIndex);

    private Lazy<TableDriveLocalTagIndex> _driveLocalTagIndex;
    public TableDriveLocalTagIndex DriveLocalTagIndex => LazyResolve(ref _driveLocalTagIndex);

    private Lazy<TableDriveReactions> _driveReactions;
    public TableDriveReactions DriveReactions => LazyResolve(ref _driveReactions);

    private Lazy<TableAppNotifications> _appNotifications;
    public TableAppNotifications AppNotifications => LazyResolve(ref _appNotifications);

    private Lazy<TableCats> _cats;
    public TableCats Cats => LazyResolve(ref _cats);

    private Lazy<TableCircle> _circle;
    public TableCircle Circle => LazyResolve(ref _circle);

    private Lazy<TableCircleMember> _circleMember;
    public TableCircleMember CircleMember => LazyResolve(ref _circleMember);

    private Lazy<TableConnections> _connections;
    public TableConnections Connections => LazyResolve(ref _connections);

    private Lazy<TableAppGrants> _appGrants;
    public TableAppGrants AppGrants => LazyResolve(ref _appGrants);

    private Lazy<TableImFollowing> _imFollowing;
    public TableImFollowing ImFollowing => LazyResolve(ref _imFollowing);

    private Lazy<TableFollowsMe> _followsMe;
    public TableFollowsMe FollowsMe => LazyResolve(ref _followsMe);

    private Lazy<TableInbox> _inbox;
    public TableInbox Inbox => LazyResolve(ref _inbox);

    private Lazy<TableOutbox> _outbox;
    public TableOutbox Outbox => LazyResolve(ref _outbox);

    private Lazy<TableKeyValue> _keyValue;
    public TableKeyValue KeyValue => LazyResolve(ref _keyValue);

    private Lazy<TableKeyTwoValue> _keyTwoValue;
    public TableKeyTwoValue KeyTwoValue => LazyResolve(ref _keyTwoValue);

    private Lazy<TableKeyThreeValue> _keyThreeValue;
    public TableKeyThreeValue KeyThreeValue => LazyResolve(ref _keyThreeValue);

    private Lazy<TableKeyUniqueThreeValue> _keyUniqueThreeValue;
    public TableKeyUniqueThreeValue KeyUniqueThreeValue => LazyResolve(ref _keyUniqueThreeValue);

    private Lazy<TableNonce> _nonce;
    public TableNonce Nonce => LazyResolve(ref _nonce);

}
