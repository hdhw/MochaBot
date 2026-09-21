using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using MochaBot.Data;
using MochaBot.Utils;

namespace MochaBot.Services;

public sealed partial class EventService
{
    private readonly DiscordSocketClient _client;
    private readonly BotDatabase _db;
    private readonly ILogger<EventService> _logger;

    public EventService(DiscordSocketClient client, BotDatabase db, ILogger<EventService> logger)
    {
        _client = client;
        _db = db;
        _logger = logger;
    }

    public void Register()
    {
        _client.UserJoined += OnUserJoined;
        _client.UserLeft += OnUserLeft;
        _client.MessageReceived += OnMessage;
        _client.ReactionAdded += OnReactionAdded;
        _client.ReactionRemoved += OnReactionRemoved;
    }

    [GeneratedRegex(@"(discord\.gg/|discord\.com/invite/|discordapp\.com/invite/)", RegexOptions.IgnoreCase)]
    private static partial Regex InviteRegex();

    [GeneratedRegex(@"https?://", RegexOptions.IgnoreCase)]
    private static partial Regex LinkRegex();

    private async Task OnUserJoined(SocketGuildUser user)
    {
        var config = _db.GetGuild(user.Guild.Id);

        if (config.AutoRoleId is { } roleId && user.Guild.GetRole(roleId) is { } role)
        {
            try { await user.AddRoleAsync(role); } catch { }
        }

        if (config.WelcomeChannelId is { } channelId
            && user.Guild.GetTextChannel(channelId) is { } channel)
        {
            var text = Format(config.WelcomeMessage, user, user.Guild);
            try { await channel.SendMessageAsync(embed: BotEmbeds.Base(null, text).WithThumbnailUrl(user.GetDisplayAvatarUrl()).Build()); }
            catch { }
        }
    }

    private async Task OnUserLeft(SocketGuild guild, SocketUser user)
    {
        var config = _db.GetGuild(guild.Id);
        if (config.GoodbyeChannelId is { } channelId && guild.GetTextChannel(channelId) is { } channel)
        {
            var text = config.GoodbyeMessage
                .Replace("{tag}", user.Username)
                .Replace("{user}", user.Mention)
                .Replace("{server}", guild.Name)
                .Replace("{count}", guild.MemberCount.ToString());
            try { await channel.SendMessageAsync(embed: BotEmbeds.Base(null, text).WithColor(BotEmbeds.Warn).Build()); }
            catch { }
        }
    }

    private async Task OnMessage(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage message || message.Author.IsBot)
            return;
        if (message.Channel is not SocketGuildChannel guildChannel)
            return;

        var config = _db.GetGuild(guildChannel.Guild.Id);

        if (message.Author is SocketGuildUser author && !author.GuildPermissions.ManageMessages)
        {
            if (await RunAutomod(message, config))
                return;
        }

        GrantXp(message, config);
    }

    private async Task<bool> RunAutomod(SocketUserMessage message, GuildConfig config)
    {
        var content = message.Content ?? "";
        var mod = config.Automod;
        string? reason = null;

        if (mod.Invites && InviteRegex().IsMatch(content)) reason = "Discord invites aren't allowed here.";
        else if (mod.Links && LinkRegex().IsMatch(content)) reason = "Links aren't allowed here.";
        else if (mod.Caps && content.Length >= 12 && CapsRatio(content) >= 0.7) reason = "Please don't shout in all caps.";
        else if (mod.BannedWords.Count > 0 && mod.BannedWords.Any(w => content.Contains(w, StringComparison.OrdinalIgnoreCase)))
            reason = "That message contained a filtered word.";

        if (reason is null)
            return false;

        try
        {
            await message.DeleteAsync();
            var notice = await message.Channel.SendMessageAsync($"{message.Author.Mention} {reason}");
            _ = Task.Delay(5000).ContinueWith(async _ => { try { await notice.DeleteAsync(); } catch { } });
        }
        catch { }
        return true;
    }

    private static double CapsRatio(string content)
    {
        var letters = content.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return 0;
        return (double)letters.Count(char.IsUpper) / letters.Count;
    }

    private void GrantXp(SocketUserMessage message, GuildConfig config)
    {
        if (message.Author is not SocketGuildUser member)
            return;

        var profile = _db.GetProfile(member.Guild.Id, member.Id);
        if ((DateTime.UtcNow - profile.LastXpGrant).TotalSeconds < 60)
            return;

        profile.LastXpGrant = DateTime.UtcNow;
        profile.Xp += Random.Shared.Next(15, 26);
        var newLevel = Leveling.LevelForXp(profile.Xp);

        if (newLevel > profile.Level)
        {
            profile.Level = newLevel;
            _db.SaveProfile(profile);
            _ = AnnounceLevelUp(member, config, newLevel);
        }
        else
        {
            _db.SaveProfile(profile);
        }
    }

    private async Task AnnounceLevelUp(SocketGuildUser member, GuildConfig config, int level)
    {
        foreach (var reward in config.LevelRewards.Where(r => r.Level <= level))
        {
            if (member.Guild.GetRole(reward.RoleId) is { } role && !member.Roles.Contains(role))
            {
                try { await member.AddRoleAsync(role); } catch { }
            }
        }

        if (!config.LevelUpAnnounce)
            return;

        var channel = (config.LevelUpChannelId is { } id ? member.Guild.GetTextChannel(id) : null);
        try
        {
            var embed = BotEmbeds.Base(null, $"🎉 {member.Mention} reached **level {level}**!").Build();
            if (channel is not null)
                await channel.SendMessageAsync(embed: embed);
        }
        catch { }
    }

    private async Task OnReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (reaction.UserId == _client.CurrentUser.Id)
            return;

        var emoji = reaction.Emote.ToString() ?? "";
        var rr = _db.ReactionRoles.FindById(ReactionRole.Key(message.Id, emoji));
        if (rr is not null && _client.GetGuild(rr.GuildId) is { } guild
            && guild.GetUser(reaction.UserId) is { } user
            && guild.GetRole(rr.RoleId) is { } role)
        {
            try { await user.AddRoleAsync(role); } catch { }
        }

        if (reaction.Emote.Name == "⭐")
            await HandleStar(message, channel);
    }

    private async Task OnReactionRemoved(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        var emoji = reaction.Emote.ToString() ?? "";
        var rr = _db.ReactionRoles.FindById(ReactionRole.Key(message.Id, emoji));
        if (rr is not null && _client.GetGuild(rr.GuildId) is { } guild
            && guild.GetUser(reaction.UserId) is { } user
            && guild.GetRole(rr.RoleId) is { } role)
        {
            try { await user.RemoveRoleAsync(role); } catch { }
        }
    }

    private async Task HandleStar(Cacheable<IUserMessage, ulong> messageCache, Cacheable<IMessageChannel, ulong> channelCache)
    {
        var channel = await channelCache.GetOrDownloadAsync();
        if (channel is not SocketGuildChannel guildChannel)
            return;

        var config = _db.GetGuild(guildChannel.Guild.Id);
        if (config.StarboardChannelId is not { } starChannelId)
            return;
        if (_client.GetChannel(starChannelId) is not IMessageChannel starChannel)
            return;

        var message = await messageCache.GetOrDownloadAsync();
        if (message is null || message.Author.IsBot)
            return;

        var star = message.Reactions.TryGetValue(new Emoji("⭐"), out var meta) ? meta.ReactionCount : 0;
        if (star < config.StarboardThreshold)
            return;

        var entry = _db.Starboard.FindById(new LiteDB.BsonValue((long)message.Id));
        var content = message.Content;
        var embed = BotEmbeds.Base(null, string.IsNullOrWhiteSpace(content) ? "*[no text]*" : content)
            .WithAuthor(message.Author.Username, message.Author.GetDisplayAvatarUrl())
            .AddField("Source", $"[Jump]({message.GetJumpUrl()})")
            .WithColor(BotEmbeds.Warn);
        if (message.Attachments.FirstOrDefault() is { } attachment)
            embed.WithImageUrl(attachment.Url);

        try
        {
            if (entry is null)
            {
                var posted = await starChannel.SendMessageAsync($"⭐ **{star}**", embed: embed.Build());
                _db.Starboard.Upsert(new StarboardEntry { Id = message.Id, StarboardMessageId = posted.Id, Stars = star });
            }
            else if (await starChannel.GetMessageAsync(entry.StarboardMessageId) is IUserMessage existing)
            {
                await existing.ModifyAsync(m => m.Content = $"⭐ **{star}**");
                entry.Stars = star;
                _db.Starboard.Upsert(entry);
            }
        }
        catch { }
    }

    public static string Format(string template, SocketGuildUser user, SocketGuild guild) =>
        template
            .Replace("{user}", user.Mention)
            .Replace("{tag}", user.Username)
            .Replace("{server}", guild.Name)
            .Replace("{count}", guild.MemberCount.ToString());
}
