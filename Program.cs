using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MochaBot.Config;
using MochaBot.Data;
using MochaBot.Services;

var config = BotConfig.Load();

var socketConfig = new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds
        | GatewayIntents.GuildMembers
        | GatewayIntents.GuildMessages
        | GatewayIntents.MessageContent
        | GatewayIntents.GuildMessageReactions
        | GatewayIntents.GuildVoiceStates,
    AlwaysDownloadUsers = true,
    LogLevel = LogSeverity.Info,
    MessageCacheSize = 200,
};

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton(config);
        services.AddSingleton<BotDatabase>();
        services.AddSingleton(new DiscordSocketClient(socketConfig));
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Async, LogLevel = LogSeverity.Info }));
        services.AddSingleton(new HttpClient());
        services.AddSingleton<MochaApiClient>();
        services.AddSingleton<PermissionService>();
        services.AddSingleton<InteractionHandler>();
        services.AddSingleton<EventService>();
        services.AddSingleton<GiveawayService>();
        services.AddHostedService<ReminderService>();
        services.AddHostedService(sp => sp.GetRequiredService<GiveawayService>());
        services.AddHostedService<ServerStatusService>();
        services.AddHostedService<DiscordBotService>();
    })
    .Build();

await host.RunAsync();
