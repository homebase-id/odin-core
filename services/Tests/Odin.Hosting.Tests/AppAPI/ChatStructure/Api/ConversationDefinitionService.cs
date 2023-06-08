using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.AppAPI.ChatStructure.Api;

/// <summary>
/// Manages chat groups and their definitions
/// </summary>
public class ConversationDefinitionService
{
    private readonly ChatServerContext _serverContext;
    private readonly ChatCommandSender _commandSender;
    private readonly Dictionary<Guid, ChatConversation> _conversationDefinitionCache;

    public ConversationDefinitionService(ChatServerContext serverContext)
    {
        _serverContext = serverContext;
        _commandSender = new ChatCommandSender(serverContext);
        _conversationDefinitionCache = new Dictionary<Guid, ChatConversation>();
    }


    public async Task<ChatConversation> StartConversation(OdinId recipient)
    {
        var senderBytes = ((OdinId)_serverContext.Sender).ToHashId().ToByteArray();
        var recipientBytes = recipient.ToHashId().ToByteArray();

        var convo = new ChatConversation()
        {
            Id = new Guid(ByteArrayUtil.EquiByteArrayXor(senderBytes, recipientBytes)),
            RecipientOdinId = recipient
        };

        await CreateConversationFileOnServer(convo);

        //send a command to the recipient
        var cmd = new JoinConversationCommand()
        {
            ConversationId = convo.Id,
            Recipients = new List<string>() { recipient }
        };

        await _commandSender.SendCommand(cmd);

        return convo;
    }

    /// <summary>
    /// Joins a conversation by creating a conversation file on my identity server linked with the sender
    /// </summary>
    public async Task JoinConversation(Guid convoId, string sender)
    {
        var convo = new ChatConversation()
        {
            Id = convoId,
            RecipientOdinId = sender
        };

        await this.CreateConversationFileOnServer(convo);
    }

    public async Task<ChatConversation> GetConversation(Guid convoId)
    {
        if (_conversationDefinitionCache.TryGetValue(convoId, out var convo))
        {
            return convo;
        }

        var (fileId, serverGroup) = await this.GetConvoFromServer(convoId);
        if (fileId != Guid.Empty)
        {
            _conversationDefinitionCache.TryAdd(serverGroup.Id, serverGroup);
            return serverGroup;
        }

        return null;
    }
    
    //

    private void UpdateConvoDefinitionCache(ChatConversation convo)
    {
        if (_conversationDefinitionCache.ContainsKey(convo.Id))
        {
            _conversationDefinitionCache[convo.Id] = convo;
        }
        else
        {
            _conversationDefinitionCache.Add(convo.Id, convo);
        }
    }

    private async Task CreateConversationFileOnServer(ChatConversation convo)
    {
        // Create the group locally
        var metadata = new UploadFileMetadata()
        {
            AccessControlList = AccessControlList.OwnerOnly,
            AppData = new UploadAppFileMetaData()
            {
                UniqueId = convo.Id,
                ContentIsComplete = true,
                JsonContent = OdinSystemSerializer.Serialize(convo),
                FileType = ChatConversation.ConversationDefinitionFileType,
                DataType = 0,
                //notice that I do not set group id here.  this file is not a converastion file, it is a definition
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

    private async Task<(Guid fileId, ChatConversation convo)> GetConvoFromServer(Guid convoId)
    {
        var qp = FileQueryParams.FromFileType(ChatApiConfig.Drive, ChatConversation.ConversationDefinitionFileType);
        var (dictionary, cursorState) = await _serverContext.QueryBatchDictionary<ChatConversation>(qp, "");

        var kvp = dictionary.SingleOrDefault(kvp => kvp.Value.Id == convoId);
        if (kvp.Key == Guid.Empty || kvp.Value == null)
        {
            return (Guid.Empty, null);
        }

        return (kvp.Key, kvp.Value);
    }
    
}