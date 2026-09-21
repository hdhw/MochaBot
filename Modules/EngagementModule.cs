using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MochaBot.Data;
using MochaBot.Utils;

namespace MochaBot.Modules;

[RequireContext(ContextType.Guild)]
public sealed class EngagementModule : InteractionModuleBase<SocketInteractionContext>
{
    private const long DailyReward = 250;
    private readonly BotDatabase _db;

    public EngagementModule(BotDatabase db) => _db = db;

    [SlashCommand("rank", "Show your (or someone's) level and XP.")]
    public async Task Rank(SocketGuildUser? user = null)
    {
        user ??= (SocketGuildUser)Context.User;
        var profile = _db.GetProfile(Context.Guild.Id, user.Id);
        var into = Leveling.XpIntoLevel(profile.Xp);
        var need = Leveling.XpToNext(profile.Xp);
        var rank = _db.Profiles.Find(p => p.GuildId == Context.Guild.Id).OrderByDescending(p => p.Xp).ToList().FindIndex(p => p.UserId == user.Id) + 1;

        var filled = (int)Math.Round(10.0 * into / Math.Max(1, need));
        var bar = new string('█', filled) + new string('░', 10 - filled);

        var embed = BotEmbeds.Base($"{user.Username}'s rank")
            .WithThumbnailUrl(user.GetDisplayAvatarUrl())
            .AddField("Level", profile.Level, true)
            .AddField("Rank", rank > 0 ? $"#{rank}" : "—", true)
            .AddField("Coins", profile.Coins, true)
            .AddField("Progress", $"`{bar}` {into}/{need} XP")
            .Build();
        await RespondAsync(embed: embed);
    }

    [SlashCommand("leaderboard", "Top members by XP.")]
    public async Task Leaderboard()
    {
        var top = _db.Profiles.Find(p => p.GuildId == Context.Guild.Id).OrderByDescending(p => p.Xp).Take(10).ToList();
        if (top.Count == 0) { await RespondAsync(embed: BotEmbeds.Info("No ranked members yet.")); return; }
        var medals = new[] { "🥇", "🥈", "🥉" };
        var lines = top.Select((p, i) => $"{(i < 3 ? medals[i] : $"`#{i + 1}`")} <@{p.UserId}> — level {p.Level} ({p.Xp} XP)");
        await RespondAsync(embed: BotEmbeds.Base("XP Leaderboard", string.Join("\n", lines)).Build());
    }

    [SlashCommand("daily", "Claim your daily coins.")]
    public async Task Daily()
    {
        var profile = _db.GetProfile(Context.Guild.Id, Context.User.Id);
        if (profile.LastDaily is { } last && (DateTime.UtcNow - last).TotalHours < 24)
        {
            var next = last.AddHours(24);
            await RespondAsync(embed: BotEmbeds.Error($"Already claimed. Come back {TimestampTag.FromDateTimeOffset(new DateTimeOffset(next, TimeSpan.Zero), TimestampTagStyles.Relative)}."), ephemeral: true);
            return;
        }
        profile.Coins += DailyReward;
        profile.LastDaily = DateTime.UtcNow;
        _db.SaveProfile(profile);
        await RespondAsync(embed: BotEmbeds.Ok($"You claimed **{DailyReward}** coins! Balance: **{profile.Coins}**."));
    }

    [SlashCommand("balance", "Check a coin balance.")]
    public async Task Balance(SocketGuildUser? user = null)
    {
        user ??= (SocketGuildUser)Context.User;
        var profile = _db.GetProfile(Context.Guild.Id, user.Id);
        await RespondAsync(embed: BotEmbeds.Base($"{user.Username}'s wallet", $"💰 **{profile.Coins}** coins").Build());
    }

    [SlashCommand("pay", "Give some of your coins to another member.")]
    public async Task Pay(SocketGuildUser user, long amount)
    {
        if (user.Id == Context.User.Id) { await RespondAsync(embed: BotEmbeds.Error("You can't pay yourself."), ephemeral: true); return; }
        if (amount <= 0) { await RespondAsync(embed: BotEmbeds.Error("Amount must be positive."), ephemeral: true); return; }
        var from = _db.GetProfile(Context.Guild.Id, Context.User.Id);
        if (from.Coins < amount) { await RespondAsync(embed: BotEmbeds.Error($"You only have {from.Coins} coins."), ephemeral: true); return; }
        var to = _db.GetProfile(Context.Guild.Id, user.Id);
        from.Coins -= amount; to.Coins += amount;
        _db.SaveProfile(from); _db.SaveProfile(to);
        await RespondAsync(embed: BotEmbeds.Ok($"Sent **{amount}** coins to {user.Mention}."));
    }
}
