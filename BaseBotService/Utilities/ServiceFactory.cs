﻿using BaseBotService.Models;
using BaseBotService.Services;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace BaseBotService.Utilities;

public static class ServiceFactory
{
    public static IServiceProvider CreateServiceProvider()
    {
        DiscordSocketConfig socketConfig = new()
        {
            LogGatewayIntentWarnings = false,
            GatewayIntents = GatewayIntents.AllUnprivileged,
            AlwaysDownloadUsers = true,
        };

        InteractionServiceConfig serviceConfig = new()
        {
            DefaultRunMode = RunMode.Async,
            InteractionCustomIdDelimiters = new[] { '.' },
            EnableAutocompleteHandlers = true,
            AutoServiceScopes = true
        };

        IServiceCollection services = new ServiceCollection()
        // log services
            .AddSingleton(LoggerFactory.CreateLogger())
            .AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()))

        // discord services
            .AddSingleton(socketConfig)
            .AddSingleton<DiscordSocketClient>()
            .AddSingleton(serviceConfig)
            .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))

        // events
            .AddSingleton<DiscordEventListener>()

        // misc services
            .AddSingleton<IAssemblyService, AssemblyService>()
            .AddSingleton<IEnvironmentService, EnvironmentService>()
            .AddScoped<IEngagementService, EngagementService>()

        // utilities
            .AddSingleton<RateLimiter>()

        // persistence services
            .AddSingleton<IPersistenceService, PersistenceService>()
            .AddScoped(MemberHC.GetServiceRegistration)
            .AddScoped(GuildMemberHC.GetServiceRegistration);

        return services.BuildServiceProvider();
    }
}