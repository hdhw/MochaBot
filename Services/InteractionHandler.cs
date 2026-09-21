using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MochaBot.Config;
using MochaBot.Data;
using MochaBot.Utils;

namespace MochaBot.Services;

public sealed class InteractionHandler
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly BotConfig _config;
    private readonly BotDatabase _db;
    private readonly ILogger<InteractionHandler> _logger;

    public InteractionHandler(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        BotConfig config,
        BotDatabase db,
        ILogger<InteractionHandler> logger)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
        _config = config;
        _db = db;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        _client.InteractionCreated += HandleInteraction;
        _interactions.SlashCommandExecuted += OnSlashExecuted;
    }

    public async Task RegisterCommandsAsync()
    {
        if (_config.GuildId is { } guildId)
            await _interactions.RegisterCommandsToGuildAsync(guildId);
        else
            await _interactions.RegisterCommandsGloballyAsync();
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interaction handling failed");
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                try { await interaction.RespondAsync(embed: BotEmbeds.Error("Something broke handling that command."), ephemeral: true); }
                catch { }
            }
        }
    }

    private async Task OnSlashExecuted(SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (command is not null)
            _db.IncrementCommand(command.Name);

        if (result.IsSuccess)
            return;

        var message = result.Error switch
        {
            InteractionCommandError.UnmetPrecondition => result.ErrorReason,
            InteractionCommandError.BadArgs => "Those arguments didn't look right.",
            _ => "Something went wrong running that command.",
        };

        try
        {
            if (context.Interaction.HasResponded)
                await context.Interaction.FollowupAsync(embed: BotEmbeds.Error(message), ephemeral: true);
            else
                await context.Interaction.RespondAsync(embed: BotEmbeds.Error(message), ephemeral: true);
        }
        catch { }
    }
}
