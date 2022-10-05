using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatCommandSender
{
    private readonly ChatServerContext _serverContext;

    public ChatCommandSender(ChatServerContext serverContext)
    {
        _serverContext = serverContext;
    }
    
    // 

    public async Task SendCommand(CommandBase cmd)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(cmd, cmd.GetType()),
                FileType = CommandBase.FileType,
                DataType = (int)cmd.Code
            },
            AccessControlList = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            }
        };

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            StorageOptions = new StorageOptions()
            {
                Drive = ChatApiConfig.Drive,
                OverwriteFileId = null,
                ExpiresTimestamp = null
            },
            TransitOptions = new TransitOptions()
            {
                Recipients = cmd.Recipients.Where(r => r != _serverContext.Sender).ToList()
            }
        };

        var result = await _serverContext.SendFile(fileMetadata, instructionSet);
        
        //TODO: consider how to handle when not all recipients received the transfer
        //examine: result.RecipientStatus[]
    }
    
}