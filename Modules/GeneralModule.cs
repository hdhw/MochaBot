using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MochaBot.Data;
using MochaBot.Utils;

namespace MochaBot.Modules;

public sealed class GeneralModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDatabase _db;

    public GeneralModule(BotDatabase db) => _db = db;

    [SlashCommand("ping", "Check the bot's latency.")]
    public async Task Ping()
    {
        var latency = Context.Client.Latency;
        await RespondAsync(embed: BotEmbeds.Base("🏓 Pong", $"Gateway latency: **{latency}ms**").Build());
    }

    [SlashCommand("help", "List everything the bot can do.")]
    public async Task Help()
    {
        var embed = BotEmbeds.Base("Mocha Bot Commands")
            .AddField("🛡️ Moderation", "`kick` `ban` `unban` `timeout` `untimeout` `warn` `warnings` `removewarning` `clearwarnings` `purge` `slowmode` `lock` `unlock` `nickname` `case` `cases`")
            .AddField("⚙️ Admin", "`config` — logs, welcome, goodbye, autorole, automod, starboard, tickets, suggestions, roles, level rewards")
            .AddField("☕ Mocha", "`player` `room` `online` `status` `shop` `toprooms` `announcements` `events` `photos`")
            .AddField("✨ Engagement", "`rank` `leaderboard` `daily` `balance` `pay`")
            .AddField("🧰 Utility", "`poll` `giveaway` `reactionrole` `embed` `tag` `suggest` `ticket` `remind` `reminders`")
            .AddField("🔗 Mocha Admin", "`link` `unlink` `whoami` `data-export` `edit-profile` (edit-profile needs a linked admin account)")
            .WithFooter("Slash commands · type / to explore")
            .Build();
        await RespondAsync(embed: embed);
    }

    [SlashCommand("userinfo", "Show info about a member.")]
    public async Task UserInfo(SocketGuildUser? user = null)
    {
        user ??= (SocketGuildUser)Context.User;
        var roles = user.Roles.Where(r => !r.IsEveryone).OrderByDescending(r => r.Position).Select(r => r.Mention);
        var embed = BotEmbeds.Base(user.Username)
            .WithThumbnailUrl(user.GetDisplayAvatarUrl(size: 256))
            .AddField("ID", user.Id, true)
            .AddField("Nickname", user.Nickname ?? "—", true)
            .AddField("Bot", user.IsBot ? "Yes" : "No", true)
            .AddField("Joined", user.JoinedAt is { } j ? TimestampTag.FromDateTimeOffset(j, TimestampTagStyles.Relative).ToString() : "—", true)
            .AddField("Created", TimestampTag.FromDateTimeOffset(user.CreatedAt, TimestampTagStyles.Relative).ToString(), true)
            .AddField($"Roles ({user.Roles.Count - 1})", roles.Any() ? string.Join(" ", roles.Take(15)) : "None")
            .Build();
        await RespondAsync(embed: embed);
    }

    [SlashCommand("serverinfo", "Show info about this server.")]
    public async Task ServerInfo()
    {
        var guild = Context.Guild;
        var embed = BotEmbeds.Base(guild.Name)
            .WithThumbnailUrl(guild.IconUrl)
            .AddField("Owner", $"<@{guild.OwnerId}>", true)
            .AddField("Members", guild.MemberCount, true)
            .AddField("Channels", guild.Channels.Count, true)
            .AddField("Roles", guild.Roles.Count, true)
            .AddField("Boosts", guild.PremiumSubscriptionCount, true)
            .AddField("Created", TimestampTag.FromDateTimeOffset(guild.CreatedAt, TimestampTagStyles.LongDate).ToString(), true)
            .Build();
        await RespondAsync(embed: embed);
    }

    [SlashCommand("avatar", "Show a user's avatar.")]
    public async Task Avatar(SocketUser? user = null)
    {
        user ??= Context.User;
        await RespondAsync(embed: BotEmbeds.Base($"{user.Username}'s avatar")
            .WithImageUrl(user.GetDisplayAvatarUrl(size: 1024)).Build());
    }

    [SlashCommand("remind", "Set a reminder.")]
    public async Task Remind(
        [Summary(description: "Examples: 10m, 2h, 3d")] string when,
        string message)
    {
        var span = DurationParser.Parse(when);
        if (span is null)
        {
            await RespondAsync(embed: BotEmbeds.Error("Couldn't read that duration. Try `10m`, `2h`, `3d`."), ephemeral: true);
            return;
        }

        var reminder = new Reminder
        {
            UserId = Context.User.Id,
            ChannelId = Context.Channel.Id,
            GuildId = Context.Guild?.Id ?? 0,
            Message = message,
            DueAt = DateTime.UtcNow + span.Value,
        };
        _db.Reminders.Insert(reminder);

        await RespondAsync(embed: BotEmbeds.Ok(
            $"I'll remind you {TimestampTag.FromDateTimeOffset(new DateTimeOffset(reminder.DueAt, TimeSpan.Zero), TimestampTagStyles.Relative)}:\n> {message}"));
    }

    [SlashCommand("reminders", "List your active reminders.")]
    public async Task Reminders()
    {
        var mine = _db.Reminders.Find(r => r.UserId == Context.User.Id).OrderBy(r => r.DueAt).Take(15).ToList();
        if (mine.Count == 0)
        {
            await RespondAsync(embed: BotEmbeds.Info("You have no active reminders."), ephemeral: true);
            return;
        }

        var lines = mine.Select(r =>
            $"`#{r.Id}` {TimestampTag.FromDateTimeOffset(new DateTimeOffset(r.DueAt, TimeSpan.Zero), TimestampTagStyles.Relative)} — {r.Message}");
        await RespondAsync(embed: BotEmbeds.Base("Your reminders", string.Join("\n", lines)).Build(), ephemeral: true);
    }

    [SlashCommand("stats", "Show bot command usage stats.")]
    public async Task Stats()
    {
        var top = _db.CommandStats.FindAll().OrderByDescending(s => s.Count).Take(10).ToList();
        var body = top.Count == 0 ? "No commands used yet." : string.Join("\n", top.Select(s => $"`/{s.Id}` — {s.Count}"));
        await RespondAsync(embed: BotEmbeds.Base("Command usage", body).Build());
    }
}
