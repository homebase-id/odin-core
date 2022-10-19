using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

/// <summary>
/// Manages chat groups and their definitions
/// </summary>
public class ConversationDefinitionService
{
    private readonly ChatServerContext _serverContext;
    private readonly ChatCommandSender _commandSender;
    private readonly Dictionary<Guid, ChatGroup> _groupCache;
    private object _chatGroupCacheLock = new();

    public ConversationDefinitionService(ChatServerContext serverContext)
    {
        _serverContext = serverContext;
        _commandSender = new ChatCommandSender(serverContext);
        _groupCache = new Dictionary<Guid, ChatGroup>();
    }

    /// <summary>
    /// Creates a group and invites members
    /// </summary>
    public async Task<bool> CreateGroup(ChatGroup group)
    {
        //validate group
        Guard.Argument(group.AdminDotYouId, nameof(group.AdminDotYouId)).NotNull().NotEmpty();
        Guard.Argument(group.Members, nameof(group.Members)).NotNull().NotEmpty();

        var (fileId, groupOnServer) = await GetGroupFromServer(group.Id);
        if (null != groupOnServer)
        {
            throw new Exception("Group already exists");
        }

        var cmd = new CreateGroupCommand()
        {
            ChatGroup = group,
            Code = CommandCode.CreateGroup,
            Recipients = group.Members.Where(m => m != _serverContext.Sender).ToList()
        };

        await _commandSender.SendCommand(cmd);

        // await this.CreateGroupOnServer(cmd.ChatGroup);
        return true;
    }

    /// <summary>
    /// Joins a group to which you've been invited
    /// </summary>
    public async Task JoinGroup(ChatGroup group)
    {
        await this.CreateGroupOnServer(group);
        UpdateCache(group);
    }

    public async Task<IEnumerable<ChatGroup>> GetGroups()
    {
        var qp = FileQueryParams.FromFileType(ChatApiConfig.Drive, ChatGroup.GroupDefinitionFileType);
        var (groups, cursorState) = await _serverContext.QueryBatch<ChatGroup>(qp, "");
        return groups;
    }

    public async Task UpdateGroup(ChatGroup group)
    {
        //get the fileId for the group.
        var qp = FileQueryParams.FromFileType(ChatApiConfig.Drive, ChatGroup.GroupDefinitionFileType);
        var (dictionary, cursorState) = await _serverContext.QueryBatchDictionary<ChatGroup>(qp, "");

        var kvp = dictionary.SingleOrDefault(kvp => kvp.Value.Id == group.Id);
        if (kvp.Key == Guid.Empty || kvp.Value == null)
        {
            throw new Exception("Group is not on server");
        }

        var fileId = kvp.Key;

        var metadata = new UploadFileMetadata()
        {
            AccessControlList = AccessControlList.NewOwnerOnly,
            AppData = new UploadAppFileMetaData()
            {
                ContentIsComplete = false,
                JsonContent = DotYouSystemSerializer.Serialize(group),
                FileType = ChatGroup.GroupDefinitionFileType,
                //notice that I do not set group id here.  this file is not a grouped file, it is a definition
            }
        };

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            StorageOptions = new StorageOptions()
            {
                Drive = ChatApiConfig.Drive,
                OverwriteFileId = fileId
            }
        };

        await _serverContext.UploadFile(metadata, instructionSet);
        UpdateCache(group);
    }

    //

    private void UpdateCache(ChatGroup group)
    {
        if (_groupCache.ContainsKey(group.Id))
        {
            _groupCache[group.Id] = group;
        }
        else
        {
            _groupCache.Add(group.Id, group);
        }
    }

    private async Task CreateGroupOnServer(ChatGroup group)
    {
        // Create the group locally
        var metadata = new UploadFileMetadata()
        {
            AccessControlList = AccessControlList.NewOwnerOnly,
            AppData = new UploadAppFileMetaData()
            {
                ContentIsComplete = false,
                JsonContent = DotYouSystemSerializer.Serialize(group),
                FileType = ChatGroup.GroupDefinitionFileType,
                DataType = 0,
                //notice that I do not set group id here.  this file is not a grouped file, it is a definition
            }
        };

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            StorageOptions = new StorageOptions()
            {
                Drive = ChatApiConfig.Drive,
                OverwriteFileId = null
            }
        };

        await _serverContext.UploadFile(metadata, instructionSet);
    }

    private async Task<(Guid fileId, ChatGroup group)> GetGroupFromServer(Guid groupId)
    {
        var qp = FileQueryParams.FromFileType(ChatApiConfig.Drive, ChatGroup.GroupDefinitionFileType);
        var (dictionary, cursorState) = await _serverContext.QueryBatchDictionary<ChatGroup>(qp, "");

        var kvp = dictionary.SingleOrDefault(kvp => kvp.Value.Id == groupId);
        if (kvp.Key == Guid.Empty || kvp.Value == null)
        {
            return (Guid.Empty, null);
        }

        return (kvp.Key, kvp.Value);
    }

    public void RemoveMember(Guid groupId)
    {
        throw new NotImplementedException();
    }

    public async Task<ChatGroup> GetGroup(Guid groupId)
    {
        if (_groupCache.TryGetValue(groupId, out var group))
        {
            return group;
        }

        var (fileId, serverGroup) = await this.GetGroupFromServer(groupId);
        if (fileId != Guid.Empty)
        {
            _groupCache.TryAdd(serverGroup.Id, serverGroup);
            return serverGroup;
        }

        return null;
    }
}