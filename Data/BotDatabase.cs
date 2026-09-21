using LiteDB;
using MochaBot.Config;

namespace MochaBot.Data;

public sealed class BotDatabase : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly BotConfig _config;

    static BotDatabase()
    {
        BsonMapper.Global.RegisterType(
            value => (long)value,
            bson => (ulong)bson.AsInt64);
    }

    public BotDatabase(BotConfig config)
    {
        _config = config;
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        _db = new LiteDatabase(Path.Combine(dir, "mocha-bot.db"));

        Warnings.EnsureIndex(w => w.GuildId);
        Warnings.EnsureIndex(w => w.UserId);
        Cases.EnsureIndex(c => c.GuildId);
        Cases.EnsureIndex(c => c.UserId);
        Reminders.EnsureIndex(r => r.DueAt);
        Profiles.EnsureIndex(p => p.GuildId);
        Links.EnsureIndex(l => l.DiscordId, true);
        Links.EnsureIndex(l => l.MochaAccountId);
        PendingLinks.EnsureIndex(p => p.Code, true);
        PendingLinks.EnsureIndex(p => p.DiscordId);
    }

    public ILiteCollection<GuildConfig> Guilds => _db.GetCollection<GuildConfig>("guilds");
    public ILiteCollection<Warning> Warnings => _db.GetCollection<Warning>("warnings");
    public ILiteCollection<ModCase> Cases => _db.GetCollection<ModCase>("cases");
    public ILiteCollection<Reminder> Reminders => _db.GetCollection<Reminder>("reminders");
    public ILiteCollection<LinkedAccount> Links => _db.GetCollection<LinkedAccount>("links");
    public ILiteCollection<PendingLink> PendingLinks => _db.GetCollection<PendingLink>("pending_links");
    public ILiteCollection<MemberProfile> Profiles => _db.GetCollection<MemberProfile>("profiles");
    public ILiteCollection<Tag> Tags => _db.GetCollection<Tag>("tags");
    public ILiteCollection<ReactionRole> ReactionRoles => _db.GetCollection<ReactionRole>("reaction_roles");
    public ILiteCollection<Giveaway> Giveaways => _db.GetCollection<Giveaway>("giveaways");
    public ILiteCollection<CommandStat> CommandStats => _db.GetCollection<CommandStat>("command_stats");
    public ILiteCollection<StarboardEntry> Starboard => _db.GetCollection<StarboardEntry>("starboard");

    public GuildConfig GetGuild(ulong guildId)
    {
        var config = Guilds.FindById(new BsonValue((long)guildId));
        if (config is null)
        {
            config = new GuildConfig { Id = guildId, LogChannelId = _config.DefaultLogChannelId };
            Guilds.Upsert(config);
        }
        return config;
    }

    public void SaveGuild(GuildConfig config) => Guilds.Upsert(config);

    public MemberProfile GetProfile(ulong guildId, ulong userId)
    {
        var key = MemberProfile.Key(guildId, userId);
        var profile = Profiles.FindById(key);
        if (profile is null)
        {
            profile = new MemberProfile { Id = key, GuildId = guildId, UserId = userId };
            Profiles.Upsert(profile);
        }
        return profile;
    }

    public void SaveProfile(MemberProfile profile) => Profiles.Upsert(profile);

    public int NextWarningId(ulong guildId)
    {
        var max = Warnings.Find(w => w.GuildId == guildId).Select(w => w.Id).DefaultIfEmpty(0).Max();
        return max + 1;
    }

    public void IncrementCommand(string name)
    {
        var stat = CommandStats.FindById(name) ?? new CommandStat { Id = name };
        stat.Count++;
        CommandStats.Upsert(stat);
    }

    public void Dispose() => _db.Dispose();
}
