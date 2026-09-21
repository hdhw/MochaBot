using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using MochaBot.Services;

namespace MochaBot.Preconditions;

public sealed class RequireLevelAttribute : PreconditionAttribute
{
    private readonly PermissionLevel _level;

    public RequireLevelAttribute(PermissionLevel level) => _level = level;

    public override Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        if (context.User is not SocketGuildUser user)
            return Task.FromResult(PreconditionResult.FromError("This command only works in a server."));

        var permissions = services.GetRequiredService<PermissionService>();
        if (permissions.Has(user, _level))
            return Task.FromResult(PreconditionResult.FromSuccess());

        var need = _level switch
        {
            PermissionLevel.Moderator => "moderator",
            PermissionLevel.Admin => "admin",
            PermissionLevel.Owner => "owner",
            _ => "elevated",
        };
        return Task.FromResult(PreconditionResult.FromError($"You need {need} permissions to use this."));
    }
}
