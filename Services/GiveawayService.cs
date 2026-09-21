using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MochaBot.Data;
using MochaBot.Utils;

namespace MochaBot.Services;

public sealed class GiveawayService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly BotDatabase _db;
    private readonly ILogger<GiveawayService> _logger;

    public GiveawayService(DiscordSocketClient client, BotDatabase db, ILogger<GiveawayService> logger)
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
                    await ProcessEnded();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Giveaway processing failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ContinueWith(_ => { });
        }
    }

    private async Task ProcessEnded()
    {
        var now = DateTime.UtcNow;
        var ended = _db.Giveaways.Find(g => !g.Ended && g.EndsAt <= now).ToList();
        foreach (var giveaway in ended)
            await Conclude(giveaway);
    }

    public async Task Conclude(Giveaway giveaway)
    {
        giveaway.Ended = true;
        _db.Giveaways.Update(giveaway);

        if (_client.GetChannel(giveaway.ChannelId) is not IMessageChannel channel)
            return;

        var entrants = new List<ulong>();
        try
        {
            if (await channel.GetMessageAsync(giveaway.MessageId) is IUserMessage entryMessage)
            {
                var reactors = await entryMessage.GetReactionUsersAsync(new Emoji("🎉"), 200).FlattenAsync();
                entrants = reactors.Where(u => !u.IsBot).Select(u => u.Id).ToList();
            }
        }
        catch { }

        var winners = PickWinners(entrants, giveaway.Winners);
        var announcement = winners.Count == 0
            ? $"🎉 Giveaway for **{giveaway.Prize}** ended, but nobody entered."
            : $"🎉 **{giveaway.Prize}** goes to {string.Join(", ", winners.Select(id => $"<@{id}>"))}! Congrats!";

        try
        {
            await channel.SendMessageAsync(announcement);
            if (await channel.GetMessageAsync(giveaway.MessageId) is IUserMessage message)
            {
                var embed = message.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? BotEmbeds.Base("Giveaway ended");
                embed.WithColor(BotEmbeds.Warn)
                    .WithTitle($"🎉 {giveaway.Prize} (ended)")
                    .WithDescription(winners.Count == 0 ? "No winners." : $"Winner(s): {string.Join(", ", winners.Select(id => $"<@{id}>"))}");
                await message.ModifyAsync(m => m.Embed = embed.Build());
            }
        }
        catch { }
    }

    private static List<ulong> PickWinners(List<ulong> entries, int count)
    {
        var pool = entries.Distinct().ToList();
        var winners = new List<ulong>();
        var rng = Random.Shared;
        while (pool.Count > 0 && winners.Count < count)
        {
            var index = rng.Next(pool.Count);
            winners.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return winners;
    }
}
