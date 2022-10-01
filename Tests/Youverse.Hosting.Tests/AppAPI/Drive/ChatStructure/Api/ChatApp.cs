using System;
using System.Collections.Generic;
using System.Data.Entity.Utilities;
using MagicOnion;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatData
{
    public Dictionary<Guid, ChatGroupMessage> GroupMessages { get; } = new();
    public List<ChatMessage> Messages { get; } = new List<ChatMessage>();

    public List<ChatGroup> Groups { get; } = new List<ChatGroup>();
}

public class ChatApp
{
    private TestSampleAppContext _appContext;
    private Dictionary<Guid, List<string>> _groups;
    private ChatContext _chatContext;
    private ChatData _chatData;

    private string _latestCursor = "";
    public ChatCommandService CommandService { get; }
    public ChatMessageService MessageService { get; }

    public ChatApp(TestSampleAppContext appContext, WebScaffold scaffold)
    {
        _appContext = appContext;
        _chatContext = new ChatContext(appContext, scaffold);
        CommandService = new ChatCommandService(_chatContext);
        MessageService = new ChatMessageService(_chatContext);
    }

    public void SynchronizeData()
    {
        //Get all chat files
        var queryParams = new FileQueryParams()
        {
            FileType = new List<int>() { ChatMessage.FileType, CommandBase.FileType }
        };

        var qbr = _chatContext.QueryBatch(queryParams, _latestCursor).GetAwaiter().GetResult();

        _latestCursor = qbr.CursorState;
        foreach (var clientFileHeader in qbr.SearchResults)
        {
            //route the incoming file
            var appData = clientFileHeader.FileMetadata.AppData;

            //if command - process the command; and wait
            if (appData.FileType == CommandBase.FileType)
            {
                this.CommandService.HandleCommandFile(clientFileHeader);
                return;
            }

            //if chat message, put into list
            if (appData.FileType == ChatMessage.FileType)
            {
                //clientFileHeader.FileMetadata.SenderDotYouId
                var msg = DotYouSystemSerializer.Deserialize<ChatMessage>(appData.JsonContent);
                HandleMessage(msg);
                return;
            }

            if (appData.FileType == ChatGroupMessage.FileType)
            {
                var msg = DotYouSystemSerializer.Deserialize<ChatGroupMessage>(appData.JsonContent);
                HandleGroupMessage(msg);
                return;
            }
        }
    }

    private void HandleGroupMessage(ChatGroupMessage msg)
    {
        _chatData.GroupMessages.Add(msg.GroupId, msg);
    }

    private void HandleMessage(ChatMessage msg)
    {
        _chatData.Messages.Add(msg);
    }
}