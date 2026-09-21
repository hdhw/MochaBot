using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MochaBot.Data;
using MochaBot.Utils;

namespace MochaBot.Services;

public sealed class ReminderService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly BotDatabase _db;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(DiscordSocketClient client, BotDatabase db, ILogger<ReminderService> logger)
    {
        _client = client;
        _db = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client.ConnectionState == ConnectionState.Connected)
                    await ProcessDue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder processing failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task ProcessDue()
    {
        var now = DateTime.UtcNow;
        var due = _db.Reminders.Find(r => r.DueAt <= now).ToList();
        foreach (var reminder in due)
        {
            var channel = _client.GetChannel(reminder.ChannelId) as IMessageChannel;
            var embed = BotEmbeds.Base("⏰ Reminder", reminder.Message)
                .WithTimestamp(new DateTimeOffset(reminder.DueAt, TimeSpan.Zero))
                .Build();
            try
            {
                if (channel is not null)
                    await channel.SendMessageAsync($"<@{reminder.UserId}>", embed: embed);
                else if (await _client.GetUserAsync(reminder.UserId) is { } user)
                    await user.SendMessageAsync(embed: embed);
            }
            catch { }

            _db.Reminders.Delete(reminder.Id);
        }
    }
}
