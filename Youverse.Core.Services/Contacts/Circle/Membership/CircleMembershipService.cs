using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Definition;
using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite.KeyValue;

namespace Youverse.Core.Services.Contacts.Circle.Membership;

public class CircleMembershipService
{
    private readonly CircleDefinitionService _circleDefinitionService;
    private readonly CircleNetworkService _circleNetworkService;
    private readonly TableCircleMember _circleMemberStorage;
    private readonly DotYouContextAccessor _contextAccessor;

    public CircleMembershipService(CircleDefinitionService circleDefinitionService, CircleNetworkService circleNetworkService, ISystemStorage systemStorage, DotYouContextAccessor contextAccessor)
    {
        _circleDefinitionService = circleDefinitionService;
        _circleNetworkService = circleNetworkService;
        _contextAccessor = contextAccessor;

        _circleMemberStorage = new TableCircleMember(systemStorage.GetDBInstance());
        _circleMemberStorage.EnsureTableExists(false);
    }

    public async Task AddCircleMember(ByteArrayId circleId, DotYouIdentity dotYouId)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ManageCircleMembership);

        //circle must exist
        var circleDefinition = _circleDefinitionService.GetCircle(circleId);
        Guard.Argument(circleDefinition, nameof(circleDefinition)).NotNull();

        if (this.IsMember(circleId, dotYouId))
        {
            throw new YouverseException($"{dotYouId} is already member of circle {circleDefinition.Name}");
        }

        await _circleNetworkService.GrantCircle(circleId, dotYouId);

        _circleMemberStorage.AddMembers(circleId.Value, new List<byte[]>() { dotYouId.ToByteArrayId().Value });
    }

    public async Task AddCircleMember(ByteArrayId circleId, IEnumerable<DotYouIdentity> list)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ManageCircleMembership);

        foreach (var dotYouId in list)
        {
            await this.AddCircleMember(circleId, dotYouId);
        }
    }

    public bool IsMember(ByteArrayId circleId, DotYouIdentity dotYouId)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ReadCircleMembership);

        //Note: need more efficient way to get members rather than pulling back the whole list
        var memberBytesList = _circleMemberStorage.GetMembers(circleId);
        return memberBytesList.Exists(id => new ByteArrayId(id) == dotYouId.ToByteArrayId());
    }

    public async Task RemoveCircleMember(ByteArrayId circleId, DotYouIdentity dotYouId)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ManageCircleMembership);

        //remove circle grants from icr
        await _circleNetworkService.RevokeCircle(circleId, dotYouId);
        _circleMemberStorage.RemoveMembers(circleId, new List<byte[]>() { dotYouId.ToByteArrayId() });
    }

    public async Task RemoveCircleMember(ByteArrayId circleId, IEnumerable<DotYouIdentity> list)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ManageCircleMembership);

        foreach (var dotYouId in list)
        {
            await this.RemoveCircleMember(circleId, dotYouId);
        }
    }

    public async Task<IEnumerable<DotYouIdentity>> GetMembers(ByteArrayId circleId)
    {
        _contextAccessor.GetCurrent().PermissionsContext.AssertHasPermission(PermissionFlags.ReadCircleMembership);
        
        var memberBytesList = _circleMemberStorage.GetMembers(circleId);
        return memberBytesList.Select(id => DotYouIdentity.FromByteArrayId(new ByteArrayId(id)));
    }
}