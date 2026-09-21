using System.Text.Json;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MochaBot.Data;
using MochaBot.Preconditions;
using MochaBot.Services;
using MochaBot.Utils;

namespace MochaBot.Modules;

[RequireContext(ContextType.Guild)]
public sealed class MochaAdminModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotDatabase _db;
    private readonly MochaApiClient _mocha;
    private readonly PermissionService _permissions;

    public MochaAdminModule(BotDatabase db, MochaApiClient mocha, PermissionService permissions)
    {
        _db = db;
        _mocha = mocha;
        _permissions = permissions;
    }

    [SlashCommand("link", "Link your Discord to your Mocha account with a code from the website.")]
    public async Task Link([Summary(description: "The code shown on the Mocha website's Link Discord page")] string code)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var result = await _mocha.AdminActionAsync("verify-link", new { code = code.Trim(), discordId = Context.User.Id.ToString() });
            var accountId = result.Long("accountId");
            var username = result.Str("username") ?? accountId.ToString();

            _db.Links.DeleteMany(l => l.DiscordId == Context.User.Id);
            _db.Links.Insert(new LinkedAccount { DiscordId = Context.User.Id, MochaAccountId = accountId, MochaUsername = username });

            await FollowupAsync(embed: BotEmbeds.Ok($"Linked to Mocha account **@{username}** (`{accountId}`)."), ephemeral: true);
        }
        catch (MochaApiException ex)
        {
            await FollowupAsync(embed: BotEmbeds.Error(ex.Message), ephemeral: true);
        }
    }

    [SlashCommand("unlink", "Remove the link between your Discord and Mocha account.")]
    public async Task Unlink()
    {
        var removed = _db.Links.DeleteMany(l => l.DiscordId == Context.User.Id);
        await RespondAsync(embed: removed > 0 ? BotEmbeds.Ok("Unlinked.") : BotEmbeds.Info("You weren't linked."), ephemeral: true);
    }

    [SlashCommand("whoami", "Show which Mocha account you're linked to.")]
    public async Task WhoAmI()
    {
        var link = _permissions.GetLink(Context.User.Id);
        await RespondAsync(embed: link is null
            ? BotEmbeds.Info("You aren't linked. Use `/link` with a code from the website.")
            : BotEmbeds.Base("Linked account", $"**@{link.MochaUsername}** (`{link.MochaAccountId}`)\nLinked {TimestampTag.FromDateTimeOffset(new DateTimeOffset(link.LinkedAt, TimeSpan.Zero), TimestampTagStyles.Relative)}").Build(),
            ephemeral: true);
    }

    [SlashCommand("data-export", "Export everything the bot has stored about you as a file.")]
    public async Task DataExport()
    {
        await DeferAsync(ephemeral: true);

        var userId = Context.User.Id;
        var link = _db.Links.FindOne(l => l.DiscordId == userId);

        object? mochaAccount = null;
        if (link is not null)
        {
            try
            {
                var player = await _mocha.GetPlayerAsync(link.MochaAccountId.ToString());
                if (player is { } p)
                    mochaAccount = JsonSerializer.Deserialize<object>(p.GetRawText());
            }
            catch (MochaApiException)
            {
                mochaAccount = "unavailable (Mocha server could not be reached)";
            }
        }

        var profiles = _db.Profiles.Find(p => p.UserId == userId).ToList();
        var warnings = _db.Warnings.Find(w => w.UserId == userId).ToList();
        var cases = _db.Cases.Find(c => c.UserId == userId).ToList();
        var reminders = _db.Reminders.Find(r => r.UserId == userId).ToList();
        var tags = _db.Tags.Find(t => t.AuthorId == userId).ToList();
        var giveawayEntries = _db.Giveaways.Find(g => g.Entries.Contains(userId))
            .Select(g => new { g.Id, g.Prize, g.GuildId, g.ChannelId, g.EndsAt, g.Ended })
            .ToList();

        var export = new
        {
            exportedAt = DateTime.UtcNow,
            discordId = userId.ToString(),
            linkedMochaAccount = link is null ? null : new
            {
                link.MochaAccountId,
                link.MochaUsername,
                link.LinkedAt,
                account = mochaAccount,
            },
            guildProfiles = profiles,
            warnings,
            moderationCases = cases,
            reminders,
            tagsAuthored = tags,
            giveawayEntries,
        };

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await FollowupWithFileAsync(
            new FileAttachment(stream, $"mocha-data-export-{userId}.json"),
            embed: BotEmbeds.Ok("Here's everything stored about you. This includes bot data (levels, warnings, reminders, etc.) plus your linked Mocha account if you have one."),
            ephemeral: true);
    }

    [SlashCommand("edit-profile", "Edit a Mocha player's profile (linked admins only).")]
    [RequireLevel(PermissionLevel.Admin)]
    public async Task EditProfile(
        [Summary(description: "Target Mocha account ID")] long uid,
        [Summary(description: "New display name")] string? display = null,
        [Summary(description: "New bio")] string? bio = null)
    {
        var link = _permissions.GetLink(Context.User.Id);
        if (link is null)
        {
            await RespondAsync(embed: BotEmbeds.Error("Link your Mocha account first with `/link` (admin actions are attributed to it)."), ephemeral: true);
            return;
        }
        if (display is null && bio is null)
        {
            await RespondAsync(embed: BotEmbeds.Error("Give a `display` and/or `bio` to change."), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        try
        {
            var result = await _mocha.AdminActionAsync("edit-profile", new
            {
                actorDiscordId = Context.User.Id.ToString(),
                actorMochaId = link.MochaAccountId,
                targetAccountId = uid,
                displayName = display,
                bio,
            });

            var changes = new List<string>();
            if (display is not null) changes.Add($"display → **{display}**");
            if (bio is not null) changes.Add("bio updated");
            await FollowupAsync(embed: BotEmbeds.Ok($"Updated account `{uid}`: {string.Join(", ", changes)}."), ephemeral: true);
        }
        catch (MochaApiException ex)
        {
            await FollowupAsync(embed: BotEmbeds.Error(ex.Message), ephemeral: true);
        }
    }
}
