using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatCommandService
{
    private readonly ChatContext _context;
    private const int CommandFileType = 587;

    public ChatCommandService(ChatContext context)
    {
        _context = context;
    }

    public Guid CreateGroup(string title, List<string> members)
    {
        //create a command file
        //send it

        var cmd = new CreateGroupCommand()
        {
            GroupId = Guid.NewGuid(),
            Title = title,
            Recipients = members,
            Code = CommandCode.CreateGroup
        };

        this.SendCommand(cmd).GetAwaiter().GetResult();
        return cmd.GroupId;
    }

    private async Task SendCommand(CommandBase cmd)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(cmd),
                FileType = CommandFileType
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
                Recipients = cmd.Recipients
            }
        };

        await _context.SendFile(fileMetadata, instructionSet);
    }

    private void ProcessCommands()
    {
        //use query batch to get all files of command type
        // run them thru processcommand
        

    }

    private void ProcessCommand()
    {
        //
        //TODO: need o setup command processor and create a .group meta file

    }
}