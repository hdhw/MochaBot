using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MochaBot.Data;
using MochaBot.Preconditions;
using MochaBot.Services;
using MochaBot.Utils;

namespace MochaBot.Modules;

public enum AutomodRule { Invites, Links, Caps, Spam }
public enum RoleTier { Moderator, Admin }

[RequireContext(ContextType.Guild)]
[RequireLevel(PermissionLevel.Admin)]
[Group("config", "Configure the bot for this server.")]
public sealed class AdminConfigModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDatabase _db;

    public AdminConfigModule(BotDatabase db) => _db = db;

    private GuildConfig Config => _db.GetGuild(Context.Guild.Id);
    private void Save(GuildConfig c) => _db.SaveGuild(c);

    [SlashCommand("view", "Show the current configuration.")]
    public async Task View()
    {
        var c = Config;
        string Ch(ulong? id) => id is { } v ? $"<#{v}>" : "—";
        string Rl(ulong? id) => id is { } v ? $"<@&{v}>" : "—";
        var embed = BotEmbeds.Base("Server configuration")
            .AddField("Log channel", Ch(c.LogChannelId), true)
            .AddField("Welcome", Ch(c.WelcomeChannelId), true)
            .AddField("Goodbye", Ch(c.GoodbyeChannelId), true)
            .AddField("Autorole", Rl(c.AutoRoleId), true)
            .AddField("Suggestions", Ch(c.SuggestionsChannelId), true)
            .AddField("Starboard", $"{Ch(c.StarboardChannelId)} (≥{c.StarboardThreshold}⭐)", true)
            .AddField("Tickets", $"cat {(c.TicketCategoryId is { } t ? $"`{t}`" : "—")} · {Rl(c.SupportRoleId)}", true)
            .AddField("Level-up", $"{Ch(c.LevelUpChannelId)} · {(c.LevelUpAnnounce ? "on" : "off")}", true)
            .AddField("Automod", $"invites {(c.Automod.Invites ? "✅" : "❌")} · links {(c.Automod.Links ? "✅" : "❌")} · caps {(c.Automod.Caps ? "✅" : "❌")} · spam {(c.Automod.Spam ? "✅" : "❌")}")
            .AddField("Mod roles", c.ModeratorRoleIds.Count > 0 ? string.Join(" ", c.ModeratorRoleIds.Select(r => $"<@&{r}>")) : "—")
            .AddField("Admin roles", c.AdminRoleIds.Count > 0 ? string.Join(" ", c.AdminRoleIds.Select(r => $"<@&{r}>")) : "—")
            .Build();
        await RespondAsync(embed: embed, ephemeral: true);
    }

    [SlashCommand("logs", "Set the moderation log channel.")]
    public async Task Logs(ITextChannel channel)
    {
        var c = Config; c.LogChannelId = channel.Id; Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Logs will go to {channel.Mention}."));
    }

    [SlashCommand("welcome", "Set the welcome channel and message.")]
    public async Task Welcome(ITextChannel channel, [Summary(description: "Use {user} {tag} {server} {count}")] string? message = null)
    {
        var c = Config; c.WelcomeChannelId = channel.Id;
        if (message is { Length: > 0 }) c.WelcomeMessage = message;
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Welcome messages set in {channel.Mention}."));
    }

    [SlashCommand("goodbye", "Set the goodbye channel and message.")]
    public async Task Goodbye(ITextChannel channel, [Summary(description: "Use {tag} {server} {count}")] string? message = null)
    {
        var c = Config; c.GoodbyeChannelId = channel.Id;
        if (message is { Length: > 0 }) c.GoodbyeMessage = message;
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Goodbye messages set in {channel.Mention}."));
    }

    [SlashCommand("autorole", "Set (or clear) the role given to new members.")]
    public async Task AutoRole(SocketRole? role = null)
    {
        var c = Config; c.AutoRoleId = role?.Id; Save(c);
        await RespondAsync(embed: BotEmbeds.Ok(role is null ? "Autorole cleared." : $"New members get {role.Mention}."));
    }

    [SlashCommand("suggestions", "Set the suggestions channel.")]
    public async Task Suggestions(ITextChannel channel)
    {
        var c = Config; c.SuggestionsChannelId = channel.Id; Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Suggestions go to {channel.Mention}."));
    }

    [SlashCommand("tickets", "Set the ticket category and support role.")]
    public async Task Tickets([Summary(description: "Category channel ID")] string categoryId, SocketRole supportRole)
    {
        if (!ulong.TryParse(categoryId, out var cat)) { await RespondAsync(embed: BotEmbeds.Error("Invalid category ID."), ephemeral: true); return; }
        var c = Config; c.TicketCategoryId = cat; c.SupportRoleId = supportRole.Id; Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Tickets open under category `{cat}`, pinging {supportRole.Mention}."));
    }

    [SlashCommand("automod", "Toggle an automod rule.")]
    public async Task Automod(AutomodRule rule, bool enabled)
    {
        var c = Config;
        switch (rule)
        {
            case AutomodRule.Invites: c.Automod.Invites = enabled; break;
            case AutomodRule.Links: c.Automod.Links = enabled; break;
            case AutomodRule.Caps: c.Automod.Caps = enabled; break;
            case AutomodRule.Spam: c.Automod.Spam = enabled; break;
        }
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Automod **{rule}** is now {(enabled ? "on" : "off")}."));
    }

    [SlashCommand("filter-add", "Add a banned word to the automod filter.")]
    public async Task FilterAdd(string word)
    {
        var c = Config;
        word = word.Trim().ToLowerInvariant();
        if (!c.Automod.BannedWords.Contains(word)) c.Automod.BannedWords.Add(word);
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Added `{word}` to the filter ({c.Automod.BannedWords.Count} words)."), ephemeral: true);
    }

    [SlashCommand("filter-remove", "Remove a banned word from the filter.")]
    public async Task FilterRemove(string word)
    {
        var c = Config;
        c.Automod.BannedWords.RemoveAll(w => w == word.Trim().ToLowerInvariant());
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Removed `{word}` from the filter."), ephemeral: true);
    }

    [SlashCommand("starboard", "Set the starboard channel and star threshold.")]
    public async Task Starboard(ITextChannel channel, int threshold = 3)
    {
        var c = Config; c.StarboardChannelId = channel.Id; c.StarboardThreshold = Math.Max(1, threshold); Save(c);
        await RespondAsync(embed: BotEmbeds.Ok($"Starboard set to {channel.Mention} at ≥{c.StarboardThreshold}⭐."));
    }

    [SlashCommand("levelup", "Set the level-up announcement channel.")]
    public async Task LevelUp(ITextChannel? channel = null, bool announce = true)
    {
        var c = Config; c.LevelUpChannelId = channel?.Id; c.LevelUpAnnounce = announce; Save(c);
        await RespondAsync(embed: BotEmbeds.Ok(announce ? $"Level-ups announced in {(channel?.Mention ?? "the current channel")}." : "Level-up announcements off."));
    }

    [SlashCommand("levelreward", "Grant a role automatically at a level.")]
    public async Task LevelReward(int level, SocketRole role, bool remove = false)
    {
        var c = Config;
        c.LevelRewards.RemoveAll(r => r.Level == level);
        if (!remove) c.LevelRewards.Add(new LevelReward { Level = level, RoleId = role.Id });
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok(remove ? $"Removed the reward at level {level}." : $"{role.Mention} will be granted at level {level}."));
    }

    [SlashCommand("role", "Add or remove a mod/admin role for bot permissions.")]
    public async Task Role(RoleTier tier, SocketRole role, bool remove = false)
    {
        var c = Config;
        var list = tier == RoleTier.Admin ? c.AdminRoleIds : c.ModeratorRoleIds;
        list.RemoveAll(r => r == role.Id);
        if (!remove) list.Add(role.Id);
        Save(c);
        await RespondAsync(embed: BotEmbeds.Ok(remove ? $"{role.Mention} is no longer a {tier} role." : $"{role.Mention} is now a {tier} role."));
    }
}
