using Discord;
using Discord.WebSocket;
using MochaBot.Config;
using MochaBot.Data;

namespace MochaBot.Services;

public enum PermissionLevel
{
    Everyone = 0,
    Moderator = 1,
    Admin = 2,
    Owner = 3,
}

public sealed class PermissionService
{
    private readonly BotConfig _config;
    private readonly BotDatabase _db;

    public PermissionService(BotConfig config, BotDatabase db)
    {
        _config = config;
        _db = db;
    }

    public PermissionLevel LevelOf(SocketGuildUser user)
    {
        if (_config.OwnerIds.Contains(user.Id) || user.Guild.OwnerId == user.Id)
            return PermissionLevel.Owner;

        var config = _db.GetGuild(user.Guild.Id);
        var roleIds = user.Roles.Select(r => r.Id).ToHashSet();

        if (user.GuildPermissions.Administrator || config.AdminRoleIds.Any(roleIds.Contains))
            return PermissionLevel.Admin;

        if (user.GuildPermissions.ModerateMembers
            || user.GuildPermissions.KickMembers
            || user.GuildPermissions.BanMembers
            || config.ModeratorRoleIds.Any(roleIds.Contains))
            return PermissionLevel.Moderator;

        return PermissionLevel.Everyone;
    }

    public bool Has(SocketGuildUser user, PermissionLevel required) => LevelOf(user) >= required;

    public LinkedAccount? GetLink(ulong discordId) =>
        _db.Links.FindOne(l => l.DiscordId == discordId);
}
