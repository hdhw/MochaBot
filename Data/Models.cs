namespace MochaBot.Data;

public sealed class GuildConfig
{
    public ulong Id { get; set; }
    public ulong? LogChannelId { get; set; }
    public ulong? WelcomeChannelId { get; set; }
    public string WelcomeMessage { get; set; } = "Welcome {user} to **{server}**! You are member **#{count}** 🎉";
    public ulong? GoodbyeChannelId { get; set; }
    public string GoodbyeMessage { get; set; } = "**{tag}** left **{server}**. We now have **{count}** members.";
    public ulong? AutoRoleId { get; set; }
    public ulong? SuggestionsChannelId { get; set; }
    public ulong? SupportRoleId { get; set; }
    public ulong? TicketCategoryId { get; set; }
    public ulong? StarboardChannelId { get; set; }
    public int StarboardThreshold { get; set; } = 3;
    public ulong? LevelUpChannelId { get; set; }
    public bool LevelUpAnnounce { get; set; } = true;

    public AutomodSettings Automod { get; set; } = new();

    public List<ulong> ModeratorRoleIds { get; set; } = new();
    public List<ulong> AdminRoleIds { get; set; } = new();
    public List<LevelReward> LevelRewards { get; set; } = new();
}

public sealed class AutomodSettings
{
    public bool Invites { get; set; }
    public bool Links { get; set; }
    public bool Caps { get; set; }
    public bool Spam { get; set; }
    public List<string> BannedWords { get; set; } = new();
}

public sealed class LevelReward
{
    public int Level { get; set; }
    public ulong RoleId { get; set; }
}

public sealed class Warning
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public ulong ModeratorId { get; set; }
    public string Reason { get; set; } = "No reason provided.";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ModCase
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public ulong ModeratorId { get; set; }
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "No reason provided.";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class Reminder
{
    public int Id { get; set; }
    public ulong UserId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong GuildId { get; set; }
    public string Message { get; set; } = "";
    public DateTime DueAt { get; set; }
}

public sealed class LinkedAccount
{
    public ulong DiscordId { get; set; }
    public long MochaAccountId { get; set; }
    public string MochaUsername { get; set; } = "";
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}

public sealed class PendingLink
{
    public string Code { get; set; } = "";
    public ulong DiscordId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public sealed class MemberProfile
{
    public string Id { get; set; } = "";
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public long Xp { get; set; }
    public int Level { get; set; }
    public long Coins { get; set; }
    public DateTime? LastDaily { get; set; }
    public DateTime LastXpGrant { get; set; } = DateTime.MinValue;

    public static string Key(ulong guildId, ulong userId) => $"{guildId}:{userId}";
}

public sealed class Tag
{
    public string Id { get; set; } = "";
    public ulong GuildId { get; set; }
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public ulong AuthorId { get; set; }
    public long Uses { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static string Key(ulong guildId, string name) => $"{guildId}:{name.ToLowerInvariant()}";
}

public sealed class ReactionRole
{
    public string Id { get; set; } = "";
    public ulong GuildId { get; set; }
    public ulong MessageId { get; set; }
    public string Emoji { get; set; } = "";
    public ulong RoleId { get; set; }

    public static string Key(ulong messageId, string emoji) => $"{messageId}:{emoji}";
}

public sealed class Giveaway
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }
    public ulong HostId { get; set; }
    public string Prize { get; set; } = "";
    public int Winners { get; set; } = 1;
    public DateTime EndsAt { get; set; }
    public bool Ended { get; set; }
    public List<ulong> Entries { get; set; } = new();
}

public sealed class CommandStat
{
    public string Id { get; set; } = "";
    public long Count { get; set; }
}

public sealed class StarboardEntry
{
    public ulong Id { get; set; }
    public ulong StarboardMessageId { get; set; }
    public int Stars { get; set; }
}
