using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MochaBot.Config;
using MochaBot.Utils;

namespace MochaBot.Services;

public sealed class ServerStatusService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly MochaApiClient _mocha;
    private readonly BotConfig _config;
    private readonly ILogger<ServerStatusService> _logger;
    private ulong _statusMessageId;
    private int _lastOnline = -1;

    public ServerStatusService(DiscordSocketClient client, MochaApiClient mocha, BotConfig config, ILogger<ServerStatusService> logger)
    {
        _client = client;
        _mocha = mocha;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(_ => { });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client.ConnectionState == ConnectionState.Connected)
                    await Update();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Status update failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task Update()
    {
        var status = await _mocha.GetStatusAsync();
        var online = status.Int("onlineCount", status.Int("players", status.Int("playerCount", status.Int("online"))));
        var rooms = status.Int("roomCount", status.Int("rooms"));
        var up = status.Bool("online", true) || online >= 0;

        if (_config.OnlinePlayersChannelId != 0
            && _client.GetChannel(_config.OnlinePlayersChannelId) is SocketVoiceChannel voice
            && online != _lastOnline)
        {
            try
            {
                await voice.ModifyAsync(c => c.Name = $"🟢 Online: {online}");
                _lastOnline = online;
            }
            catch { }
        }

        if (_config.StatusChannelId != 0 && _client.GetChannel(_config.StatusChannelId) is IMessageChannel channel)
        {
            var embed = BotEmbeds.Base("Mocha Server Status")
                .WithColor(up ? BotEmbeds.Success : BotEmbeds.Danger)
                .AddField("State", up ? "🟢 Online" : "🔴 Offline", true)
                .AddField("Players", online.ToString(), true)
                .AddField("Rooms", rooms > 0 ? rooms.ToString() : "—", true)
                .WithFooter("Updated")
                .WithCurrentTimestamp()
                .Build();

            try
            {
                if (_statusMessageId != 0 && await channel.GetMessageAsync(_statusMessageId) is IUserMessage existing)
                {
                    await existing.ModifyAsync(m => m.Embed = embed);
                }
                else
                {
                    var sent = await channel.SendMessageAsync(embed: embed);
                    _statusMessageId = sent.Id;
                }
            }
            catch { }
        }
    }
}
