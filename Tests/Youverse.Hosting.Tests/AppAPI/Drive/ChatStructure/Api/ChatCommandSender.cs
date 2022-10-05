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
                DataType = (int)cmd.Code,
                GroupId = cmd.GroupId
            },
            AccessControlList = AccessControlList.NewOwnerOnly
        };

        var instructionSet = UploadInstructionSet.NewWithRecipients(ChatApiConfig.Drive, cmd.Recipients.Where(r => r != _serverContext.Sender));
        var result = await _serverContext.SendFile(fileMetadata, instructionSet);

        //TODO: consider how to handle when not all recipients received the transfer
        //examine: result.RecipientStatus[]
    }
}