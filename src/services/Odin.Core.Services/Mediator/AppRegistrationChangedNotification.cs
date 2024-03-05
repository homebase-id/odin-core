using System;
using MediatR;
using Odin.Core.Services.Authorization.Apps;

namespace Odin.Core.Services.Mediator;

public class AppRegistrationChangedNotification : EventArgs, INotification
{
    public AppRegistration NewAppRegistration { get; set; }
    public AppRegistration OldAppRegistration { get; set; }
}