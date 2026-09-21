using System.Text.Json;
using Discord;
using Discord.Interactions;
using MochaBot.Services;
using MochaBot.Utils;

namespace MochaBot.Modules;

public sealed class MochaModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly string[] AccessibilityNames = { "Private", "Public", "Unlisted", "Dev only", "Dev unlisted" };
    private readonly MochaApiClient _mocha;

    public MochaModule(MochaApiClient mocha) => _mocha = mocha;

    private async Task<bool> Guard(Func<Task> action)
    {
        try { await action(); return true; }
        catch (MochaApiException ex)
        {
            if (Context.Interaction.HasResponded)
                await FollowupAsync(embed: BotEmbeds.Error(ex.Message), ephemeral: true);
            else
                await RespondAsync(embed: BotEmbeds.Error(ex.Message), ephemeral: true);
            return false;
        }
    }

    [SlashCommand("player", "Look up a Mocha player.")]
    public async Task Player([Summary(description: "Username or account ID")] string query)
    {
        await DeferAsync();
        await Guard(async () =>
        {
            var player = await _mocha.GetPlayerAsync(query);
            if (player is not { } p)
            {
                await FollowupAsync(embed: BotEmbeds.Error($"No player found for **{query}**."));
                return;
            }

            var accountId = p.Long("accountId");
            var embed = BotEmbeds.Base($"@{p.Str("username")}")
                .WithUrl(_mocha.RecNetLink($"user/{p.Str("username")}"))
                .AddField("Display name", p.Str("displayName") ?? "—", true)
                .AddField("Account ID", accountId, true);

            if (p.Str("bio") is { Length: > 0 } bio)
                embed.AddField("Bio", bio.Length > 300 ? bio[..300] : bio);

            var image = p.Str("profileImage") ?? p.Str("imageName") ?? p.Str("profilePicture");
            if (_mocha.AssetUrl(image) is { } url)
                embed.WithThumbnailUrl(url);

            await FollowupAsync(embed: embed.Build());
        });
    }

    [SlashCommand("room", "Look up a Mocha room.")]
    public async Task Room([Summary(description: "Room name or ID")] string query)
    {
        await DeferAsync();
        await Guard(async () =>
        {
            var room = await _mocha.GetRoomAsync(query);
            if (room is not { } r)
            {
                await FollowupAsync(embed: BotEmbeds.Error($"No room found for **{query}**."));
                return;
            }

            var accessibility = r.Int("accessibility");
            var stats = r.Sub("stats");
            var embed = BotEmbeds.Base($"^{r.Str("name")}")
                .WithUrl(_mocha.RecNetLink($"room/{r.Str("name")}"))
                .WithDescription(r.Str("description") is { Length: > 0 } d ? d : "*No description*")
                .AddField("Room ID", r.Long("roomId"), true)
                .AddField("Access", accessibility >= 0 && accessibility < AccessibilityNames.Length ? AccessibilityNames[accessibility] : accessibility.ToString(), true)
                .AddField("Max players", r.Int("maxPlayers"), true)
                .AddField("Visits", (stats?.Long("visitCount") ?? r.Long("visitCount")), true)
                .AddField("Favorites", (stats?.Long("favoriteCount") ?? r.Long("favoriteCount")), true)
                .AddField("Cloning", r.Bool("cloningAllowed") ? "Yes" : "No", true);

            if (_mocha.AssetUrl(r.Str("imageName")) is { } url)
                embed.WithImageUrl(url);

            await FollowupAsync(embed: embed.Build());
        });
    }

    [SlashCommand("status", "Show the live Mocha server status.")]
    public async Task Status()
    {
        await DeferAsync();
        await Guard(async () =>
        {
            var status = await _mocha.GetStatusAsync();
            var online = status.Int("onlineCount", status.Int("players", status.Int("playerCount", status.Int("online"))));
            var embed = BotEmbeds.Base("Mocha Server Status")
                .WithColor(BotEmbeds.Success)
                .AddField("Players online", online, true)
                .AddField("Rooms", status.Int("roomCount", status.Int("rooms")), true)
                .WithCurrentTimestamp()
                .Build();
            await FollowupAsync(embed: embed);
        });
    }

    [SlashCommand("events", "List upcoming Mocha events.")]
    public async Task Events()
    {
        await DeferAsync();
        await Guard(async () =>
        {
            var events = await _mocha.GetEventsAsync();
            if (events.Count == 0) { await FollowupAsync(embed: BotEmbeds.Info("No events scheduled.")); return; }
            var lines = events.Take(10).Select(e =>
            {
                var name = e.Str("name") ?? e.Str("title") ?? "Event";
                var desc = e.Str("description");
                return desc is { Length: > 0 } ? $"**{name}** — {(desc.Length > 100 ? desc[..100] : desc)}" : $"**{name}**";
            });
            await FollowupAsync(embed: BotEmbeds.Base("Mocha Events", string.Join("\n", lines)).Build());
        });
    }

    [SlashCommand("photos", "Show the newest Mocha photos.")]
    public async Task Photos([Summary(description: "How many, 1-10")] int count = 5)
    {
        await DeferAsync();
        await Guard(async () =>
        {
            var photos = await _mocha.GetPhotosAsync(Math.Clamp(count, 1, 10));
            if (photos.Count == 0) { await FollowupAsync(embed: BotEmbeds.Info("No photos yet.")); return; }

            var embeds = photos.Take(Math.Clamp(count, 1, 10))
                .Select(p => _mocha.AssetUrl(p.Str("imageName") ?? p.Str("image")))
                .Where(u => u is not null)
                .Select(u => BotEmbeds.Base().WithUrl("https://rec.net").WithImageUrl(u).Build())
                .ToArray();

            if (embeds.Length == 0) { await FollowupAsync(embed: BotEmbeds.Info("No displayable photos.")); return; }
            await FollowupAsync(embeds: embeds);
        });
    }
}
