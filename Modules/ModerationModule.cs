using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MochaBot.Data;
using MochaBot.Preconditions;
using MochaBot.Services;
using MochaBot.Utils;

namespace MochaBot.Modules;

[RequireContext(ContextType.Guild)]
[RequireLevel(PermissionLevel.Moderator)]
public sealed class ModerationModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDatabase _db;

    public ModerationModule(BotDatabase db) => _db = db;

    private int LogCase(ulong userId, string action, string reason, DateTime? expires = null)
    {
        var c = new ModCase
        {
            GuildId = Context.Guild.Id,
            UserId = userId,
            ModeratorId = Context.User.Id,
            Action = action,
            Reason = reason,
            ExpiresAt = expires,
        };
        _db.Cases.Insert(c);
        _ = PostLog(c);
        return c.Id;
    }

    private async Task PostLog(ModCase c)
    {
        var config = _db.GetGuild(Context.Guild.Id);
        if (config.LogChannelId is not { } id || Context.Guild.GetTextChannel(id) is not { } channel)
            return;
        var embed = BotEmbeds.Base($"Case #{c.Id} · {c.Action}")
            .WithColor(BotEmbeds.Warn)
            .AddField("User", $"<@{c.UserId}> (`{c.UserId}`)", true)
            .AddField("Moderator", $"<@{c.ModeratorId}>", true)
            .AddField("Reason", c.Reason)
            .WithCurrentTimestamp();
        if (c.ExpiresAt is { } exp)
            embed.AddField("Expires", TimestampTag.FromDateTimeOffset(new DateTimeOffset(exp, TimeSpan.Zero), TimestampTagStyles.Relative).ToString(), true);
        try { await channel.SendMessageAsync(embed: embed.Build()); } catch { }
    }

    private bool OutranksTarget(SocketGuildUser target)
    {
        var self = (SocketGuildUser)Context.User;
        return self.Hierarchy > target.Hierarchy || Context.Guild.OwnerId == self.Id;
    }

    [SlashCommand("kick", "Kick a member from the server.")]
    [RequireBotPermission(GuildPermission.KickMembers)]
    public async Task Kick(SocketGuildUser member, string reason = "No reason provided.")
    {
        if (!OutranksTarget(member)) { await RespondAsync(embed: BotEmbeds.Error("You can't act on that member."), ephemeral: true); return; }
        await member.KickAsync(reason);
        var id = LogCase(member.Id, "Kick", reason);
        await RespondAsync(embed: BotEmbeds.Ok($"Kicked **{member.Username}** · case #{id}"));
    }

    [SlashCommand("ban", "Ban a member from the server.")]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task Ban(SocketUser user, string reason = "No reason provided.", [Summary(description: "Delete recent messages, 0-7 days")] int delete_days = 0)
    {
        if (user is SocketGuildUser member && !OutranksTarget(member)) { await RespondAsync(embed: BotEmbeds.Error("You can't act on that member."), ephemeral: true); return; }
        await Context.Guild.AddBanAsync(user, Math.Clamp(delete_days, 0, 7), reason);
        var id = LogCase(user.Id, "Ban", reason);
        await RespondAsync(embed: BotEmbeds.Ok($"Banned **{user.Username}** · case #{id}"));
    }

    [SlashCommand("unban", "Unban a user by ID.")]
    [RequireBotPermission(GuildPermission.BanMembers)]
    public async Task Unban([Summary(description: "Discord user ID")] string user_id, string reason = "No reason provided.")
    {
        if (!ulong.TryParse(user_id, out var id)) { await RespondAsync(embed: BotEmbeds.Error("That's not a valid user ID."), ephemeral: true); return; }
        if (await Context.Guild.GetBanAsync(id) is null) { await RespondAsync(embed: BotEmbeds.Error("That user isn't banned."), ephemeral: true); return; }
        await Context.Guild.RemoveBanAsync(id);
        var caseId = LogCase(id, "Unban", reason);
        await RespondAsync(embed: BotEmbeds.Ok($"Unbanned `{id}` · case #{caseId}"));
    }

    [SlashCommand("timeout", "Temporarily timeout a member.")]
    [RequireBotPermission(GuildPermission.ModerateMembers)]
    public async Task Timeout(SocketGuildUser member, [Summary(description: "Examples: 10m, 2h, 3d")] string duration, string reason = "No reason provided.")
    {
        if (!OutranksTarget(member)) { await RespondAsync(embed: BotEmbeds.Error("You can't act on that member."), ephemeral: true); return; }
        var span = DurationParser.Parse(duration);
        if (span is null || span > TimeSpan.FromDays(28)) { await RespondAsync(embed: BotEmbeds.Error("Duration must be between 1s and 28d."), ephemeral: true); return; }
        await member.SetTimeOutAsync(span.Value);
        var id = LogCase(member.Id, "Timeout", reason, DateTime.UtcNow + span.Value);
        await RespondAsync(embed: BotEmbeds.Ok($"Timed out **{member.Username}** for {DurationParser.Humanize(span.Value)} · case #{id}"));
    }

    [SlashCommand("untimeout", "Remove a member's timeout.")]
    [RequireBotPermission(GuildPermission.ModerateMembers)]
    public async Task Untimeout(SocketGuildUser member, string reason = "No reason provided.")
    {
        await member.RemoveTimeOutAsync();
        var id = LogCase(member.Id, "Untimeout", reason);
        await RespondAsync(embed: BotEmbeds.Ok($"Removed timeout from **{member.Username}** · case #{id}"));
    }

    [SlashCommand("warn", "Add a persistent warning to a member.")]
    public async Task Warn(SocketGuildUser member, string reason = "No reason provided.")
    {
        var warning = new Warning
        {
            Id = _db.NextWarningId(Context.Guild.Id),
            GuildId = Context.Guild.Id,
            UserId = member.Id,
            ModeratorId = Context.User.Id,
            Reason = reason,
        };
        _db.Warnings.Insert(warning);
        LogCase(member.Id, "Warn", reason);
        var count = _db.Warnings.Count(w => w.GuildId == Context.Guild.Id && w.UserId == member.Id);
        await RespondAsync(embed: BotEmbeds.Ok($"Warned **{member.Username}** (warning #{warning.Id}, {count} total)"));
        try { await member.SendMessageAsync(embed: BotEmbeds.Base($"You were warned in {Context.Guild.Name}", reason).WithColor(BotEmbeds.Warn).Build()); } catch { }
    }

    [SlashCommand("warnings", "View a member's warnings.")]
    public async Task Warnings(SocketGuildUser member)
    {
        var list = _db.Warnings.Find(w => w.GuildId == Context.Guild.Id && w.UserId == member.Id).OrderBy(w => w.Id).ToList();
        if (list.Count == 0) { await RespondAsync(embed: BotEmbeds.Info($"**{member.Username}** has no warnings.")); return; }
        var lines = list.Take(20).Select(w => $"`#{w.Id}` <@{w.ModeratorId}> — {w.Reason} ({TimestampTag.FromDateTimeOffset(new DateTimeOffset(w.CreatedAt, TimeSpan.Zero), TimestampTagStyles.ShortDate)})");
        await RespondAsync(embed: BotEmbeds.Base($"Warnings for {member.Username} ({list.Count})", string.Join("\n", lines)).WithColor(BotEmbeds.Warn).Build());
    }

    [SlashCommand("removewarning", "Remove one warning by ID.")]
    public async Task RemoveWarning(SocketGuildUser member, [Summary(description: "Warning ID from /warnings")] int warning_id)
    {
        var deleted = _db.Warnings.DeleteMany(w => w.GuildId == Context.Guild.Id && w.UserId == member.Id && w.Id == warning_id);
        await RespondAsync(embed: deleted > 0
            ? BotEmbeds.Ok($"Removed warning #{warning_id} from **{member.Username}**.")
            : BotEmbeds.Error($"No warning #{warning_id} on **{member.Username}**."));
    }

    [SlashCommand("clearwarnings", "Clear all of a member's warnings.")]
    public async Task ClearWarnings(SocketGuildUser member)
    {
        var deleted = _db.Warnings.DeleteMany(w => w.GuildId == Context.Guild.Id && w.UserId == member.Id);
        await RespondAsync(embed: BotEmbeds.Ok($"Cleared {deleted} warning(s) from **{member.Username}**."));
    }

    [SlashCommand("purge", "Bulk-delete recent messages in this channel.")]
    [RequireBotPermission(GuildPermission.ManageMessages)]
    public async Task Purge([Summary(description: "Number of messages, 1-100")] int amount, SocketGuildUser? member = null)
    {
        amount = Math.Clamp(amount, 1, 100);
        if (Context.Channel is not ITextChannel channel) { await RespondAsync(embed: BotEmbeds.Error("Can't purge here."), ephemeral: true); return; }
        await DeferAsync(ephemeral: true);
        var messages = (await channel.GetMessagesAsync(amount + 1).FlattenAsync())
            .Where(m => (DateTimeOffset.UtcNow - m.Timestamp).TotalDays < 14)
            .Where(m => member is null || m.Author.Id == member.Id)
            .Take(amount)
            .ToList();
        await channel.DeleteMessagesAsync(messages);
        await FollowupAsync(embed: BotEmbeds.Ok($"Deleted {messages.Count} message(s)."), ephemeral: true);
    }

    [SlashCommand("slowmode", "Set slowmode in a text channel.")]
    [RequireBotPermission(GuildPermission.ManageChannels)]
    public async Task Slowmode([Summary(description: "0 disables; max 21600")] int seconds, ITextChannel? channel = null)
    {
        channel ??= (ITextChannel)Context.Channel;
        seconds = Math.Clamp(seconds, 0, 21600);
        await channel.ModifyAsync(c => c.SlowModeInterval = seconds);
        await RespondAsync(embed: BotEmbeds.Ok(seconds == 0 ? $"Slowmode off in {channel.Mention}." : $"Slowmode set to {seconds}s in {channel.Mention}."));
    }

    [SlashCommand("lock", "Prevent @everyone from sending messages in a channel.")]
    [RequireBotPermission(GuildPermission.ManageChannels)]
    public async Task Lock(ITextChannel? channel = null)
    {
        channel ??= (ITextChannel)Context.Channel;
        await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
            new OverwritePermissions(sendMessages: PermValue.Deny));
        await RespondAsync(embed: BotEmbeds.Ok($"🔒 Locked {channel.Mention}."));
    }

    [SlashCommand("unlock", "Restore @everyone sending permissions in a channel.")]
    [RequireBotPermission(GuildPermission.ManageChannels)]
    public async Task Unlock(ITextChannel? channel = null)
    {
        channel ??= (ITextChannel)Context.Channel;
        await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole,
            new OverwritePermissions(sendMessages: PermValue.Inherit));
        await RespondAsync(embed: BotEmbeds.Ok($"🔓 Unlocked {channel.Mention}."));
    }

    [SlashCommand("nickname", "Change or reset a member's nickname.")]
    [RequireBotPermission(GuildPermission.ManageNicknames)]
    public async Task Nickname(SocketGuildUser member, [Summary(description: "Leave blank to reset")] string? nickname = null)
    {
        await member.ModifyAsync(m => m.Nickname = nickname);
        await RespondAsync(embed: BotEmbeds.Ok(nickname is null ? $"Reset **{member.Username}**'s nickname." : $"Set **{member.Username}**'s nickname to **{nickname}**."));
    }

    [SlashCommand("cases", "List a member's moderation history.")]
    public async Task Cases(SocketUser user)
    {
        var list = _db.Cases.Find(c => c.GuildId == Context.Guild.Id && c.UserId == user.Id).OrderByDescending(c => c.Id).Take(15).ToList();
        if (list.Count == 0) { await RespondAsync(embed: BotEmbeds.Info($"**{user.Username}** has no cases.")); return; }
        var lines = list.Select(c => $"`#{c.Id}` **{c.Action}** — {c.Reason} · <@{c.ModeratorId}>");
        await RespondAsync(embed: BotEmbeds.Base($"Cases for {user.Username}", string.Join("\n", lines)).Build());
    }

    [SlashCommand("case", "Look up a single moderation case.")]
    public async Task Case(int id)
    {
        var c = _db.Cases.FindOne(x => x.GuildId == Context.Guild.Id && x.Id == id);
        if (c is null) { await RespondAsync(embed: BotEmbeds.Error($"No case #{id}."), ephemeral: true); return; }
        var embed = BotEmbeds.Base($"Case #{c.Id} · {c.Action}")
            .AddField("User", $"<@{c.UserId}>", true)
            .AddField("Moderator", $"<@{c.ModeratorId}>", true)
            .AddField("When", TimestampTag.FromDateTimeOffset(new DateTimeOffset(c.CreatedAt, TimeSpan.Zero), TimestampTagStyles.LongDateTime).ToString())
            .AddField("Reason", c.Reason)
            .Build();
        await RespondAsync(embed: embed);
    }
}
