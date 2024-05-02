using System;
using MediatR;
using Odin.Core.Storage.SQLite;
using Odin.Services.Authorization.Apps;

namespace Odin.Services.Mediator;

public class AppRegistrationChangedNotification : MediatorNotificationBase
{
    public AppRegistration NewAppRegistration { get; init; }
    public AppRegistration OldAppRegistration { get; init; }
    public DatabaseConnection DatabaseConnection { get; init; }
}