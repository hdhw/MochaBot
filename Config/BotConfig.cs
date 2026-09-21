namespace MochaBot.Config;

public sealed class BotConfig
{
    public required string Token { get; init; }
    public required ulong ClientId { get; init; }
    public ulong? GuildId { get; init; }

    public ulong DefaultLogChannelId { get; init; }
    public ulong StatusChannelId { get; init; }
    public ulong OnlinePlayersChannelId { get; init; }

    public required string MochaApiUrl { get; init; }
    public required string MochaRecNetUrl { get; init; }

    public string? BotAdminSecret { get; init; }

    public IReadOnlySet<ulong> OwnerIds { get; init; } = new HashSet<ulong>();

    public string BrandColorHex { get; init; } = "bf5af2";

    public static BotConfig Load()
    {
        DotEnv.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
        DotEnv.Load(".env");

        string Require(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Missing environment variable {key}. Copy .env.example to .env first.");
            return value.Trim();
        }

        string? Optional(string key) => Environment.GetEnvironmentVariable(key)?.Trim() is { Length: > 0 } v ? v : null;

        ulong ParseId(string? value, ulong fallback = 0) =>
            ulong.TryParse(value, out var id) ? id : fallback;

        string TrimUrl(string url) => url.TrimEnd('/');

        var api = TrimUrl(Environment.GetEnvironmentVariable("MOCHA_API_URL")?.Trim() is { Length: > 0 } a
            ? a
            : "https://amplitudeshitlol.realcat.lol");

        var recnet = TrimUrl(Optional("MOCHA_RECNET_URL") ?? api);

        var owners = (Optional("OWNER_IDS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => ulong.TryParse(s, out var id) ? id : (ulong?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        return new BotConfig
        {
            Token = Require("DISCORD_TOKEN"),
            ClientId = ParseId(Require("CLIENT_ID")),
            GuildId = Optional("GUILD_ID") is { } g && ulong.TryParse(g, out var gid) ? gid : null,
            DefaultLogChannelId = ParseId(Optional("LOG_CHANNEL_ID"), 1526971616980893947),
            StatusChannelId = ParseId(Optional("STATUS_CHANNEL_ID"), 1538378952140263445),
            OnlinePlayersChannelId = ParseId(Optional("ONLINE_PLAYERS_CHANNEL_ID"), 1538378881826824292),
            MochaApiUrl = api,
            MochaRecNetUrl = recnet,
            BotAdminSecret = Optional("BOT_ADMIN_SECRET"),
            OwnerIds = owners,
        };
    }
}
