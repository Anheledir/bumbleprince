﻿using BaseBotService.Attributes;
using BaseBotService.Utilities;
using Discord.WebSocket;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace BaseBotService.Modules;
public abstract class BaseModule : InteractionModuleBase<SocketInteractionContext>
{
    // Dependencies can be accessed through Property injection,
    // public properties with public setters will be set by the service provider
    public ILogger Logger { get; set; } = null!;
    public SocketUser Caller => Context.Interaction.User;
    public SocketSelfUser BotUser => Context.Client.CurrentUser;
    public InteractionService Commands { get; set; } = null!;
    public RateLimiter RateLimiter { get; set; } = null!;
    public IAssemblyService AssemblyService { get; set; } = null!;
    public IEnvironmentService EnvironmentService { get; set; } = null!;
    public IPersistenceService PersistenceService { get; set; } = null!;
    protected EmbedBuilder GetEmbedBuilder() => new()
    {
        Author = new EmbedAuthorBuilder
        {
            Name = Caller.Username,
            IconUrl = Caller.GetAvatarUrl()
        },
        Color = Color.Gold,
        Timestamp = DateTimeOffset.UtcNow,
        Footer = new EmbedFooterBuilder
        {
            IconUrl = BotUser.GetAvatarUrl(),
            Text = $"Honeycomb v{AssemblyService.Version} ({EnvironmentService.EnvironmentName})"
        }
    };

    /// <summary>
    /// Checks if the current user is allowed to execute the specified command based on the rate limit settings.
    /// </summary>
    /// <param name="commandName">The name of the command method. Automatically derived from the calling method's name when not specified explicitly.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation, containing true if the user is allowed to execute the command, false otherwise.</returns>
    /// <example>
    /// <code>
    /// public class MySlashCommandModule : BaseBotService.Modules.BaseModule
    /// {
    ///     [SlashCommand("example", "An example slash command.")]
    ///     [RateLimit(5, 60)] // Limit to 5 calls per 60 seconds
    ///     public async Task ExampleCommandAsync()
    ///     {
    ///         if (!await CheckRateLimitAsync())
    ///         {
    ///             return;
    ///         }
    ///
    ///         // Command implementation
    ///     }
    /// }
    /// </code>
    /// </example>
    protected async Task<bool> CheckRateLimitAsync([CallerMemberName] string commandName = "")
    {
        var method = GetType().GetMethod(commandName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var rateLimitAttribute = method?.GetCustomAttribute<RateLimitAttribute>();

        if (rateLimitAttribute != null)
        {
            ulong userId = Context.User.Id;
            Logger.Debug("Checking rate limit [{MaxAttempts}/{TimeWindow}] for user {UserId} on command {CommandName}", rateLimitAttribute.MaxAttempts, rateLimitAttribute.TimeWindow, userId, commandName);

            if (!await RateLimiter.IsAllowed(userId, commandName, rateLimitAttribute.MaxAttempts, rateLimitAttribute.TimeWindow))
            {
                await ReplyAsync("You have reached the rate limit for this command. Please wait before trying again.");
                return false;
            }
        }

        return true;
    }
}
