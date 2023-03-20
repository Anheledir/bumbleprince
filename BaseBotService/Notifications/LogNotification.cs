﻿namespace BaseBotService.Notifications;

public class LogNotification : INotification
{
    public LogNotification(LogMessage message) => LogMessage = message;

    public LogMessage LogMessage { get; }
}