using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MochaBot.Config;

namespace MochaBot.Services;

public sealed class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly BotConfig _config;
    private readonly InteractionHandler _interactions;
    private readonly EventService _events;
    private readonly ILogger<DiscordBotService> _logger;

    public DiscordBotService(
        DiscordSocketClient client,
        BotConfig config,
        InteractionHandler interactions,
        EventService events,
        ILogger<DiscordBotService> logger)
    {
        _client = client;
        _config = config;
        _interactions = interactions;
        _events = events;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.Log += OnLog;
        _client.Ready += OnReady;

        await _interactions.InitializeAsync();
        _events.Register();

        await _client.LoginAsync(TokenType.Bot, _config.Token);
        await _client.StartAsync();

        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });
    }

    private async Task OnReady()
    {
        _logger.LogInformation("Logged in as {User}", _client.CurrentUser);
        await _interactions.RegisterCommandsAsync();
    }

    private Task OnLog(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };
        _logger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.LogoutAsync();
        await _client.StopAsync();
        await base.StopAsync(cancellationToken);
    }
}
