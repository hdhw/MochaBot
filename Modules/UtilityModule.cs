using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MochaBot.Data;
using MochaBot.Preconditions;
using MochaBot.Services;
using MochaBot.Utils;

namespace MochaBot.Modules;

[RequireContext(ContextType.Guild)]
public sealed class UtilityModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly string[] PollEmojis = { "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣", "🔟" };
    private readonly BotDatabase _db;

    public UtilityModule(BotDatabase db) => _db = db;

    [SlashCommand("poll", "Start a reaction poll (options separated by | ).")]
    public async Task Poll(string question, [Summary(description: "Up to 10 options separated by |")] string options)
    {
        var choices = options.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(10).ToList();
        if (choices.Count < 2) { await RespondAsync(embed: BotEmbeds.Error("Give at least two options separated by `|`."), ephemeral: true); return; }

        var body = string.Join("\n", choices.Select((c, i) => $"{PollEmojis[i]} {c}"));
        var embed = BotEmbeds.Base($"📊 {question}", body).WithFooter($"Poll by {Context.User.Username}").Build();
        await RespondAsync(embed: embed);
        var message = await GetOriginalResponseAsync();
        for (var i = 0; i < choices.Count; i++)
            await message.AddReactionAsync(new Emoji(PollEmojis[i]));
    }

    [SlashCommand("suggest", "Submit a suggestion.")]
    public async Task Suggest(string suggestion)
    {
        var config = _db.GetGuild(Context.Guild.Id);
        if (config.SuggestionsChannelId is not { } id || Context.Guild.GetTextChannel(id) is not { } channel)
        {
            await RespondAsync(embed: BotEmbeds.Error("No suggestions channel is set. Ask an admin to run `/config suggestions`."), ephemeral: true);
            return;
        }
        var embed = BotEmbeds.Base(null, suggestion)
            .WithAuthor(Context.User.Username, Context.User.GetDisplayAvatarUrl())
            .WithFooter("Suggestion").Build();
        var message = await channel.SendMessageAsync(embed: embed);
        await message.AddReactionAsync(new Emoji("👍"));
        await message.AddReactionAsync(new Emoji("👎"));
        await RespondAsync(embed: BotEmbeds.Ok("Suggestion submitted!"), ephemeral: true);
    }

    [SlashCommand("embed", "Post a custom embed (admin).")]
    [RequireLevel(PermissionLevel.Admin)]
    public async Task PostEmbed(string title, string description, ITextChannel? channel = null, string? color = null)
    {
        channel ??= (ITextChannel)Context.Channel;
        var builder = new EmbedBuilder().WithTitle(title).WithDescription(description.Replace("\\n", "\n")).WithColor(BotEmbeds.Brand);
        if (color is { Length: > 0 } && uint.TryParse(color.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            builder.WithColor(new Color(rgb));
        await channel.SendMessageAsync(embed: builder.Build());
        await RespondAsync(embed: BotEmbeds.Ok($"Posted to {channel.Mention}."), ephemeral: true);
    }

    [Group("tag", "Reusable saved snippets.")]
    public sealed class TagModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly BotDatabase _db;
        public TagModule(BotDatabase db) => _db = db;

        [SlashCommand("show", "Show a tag.")]
        public async Task Show(string name)
        {
            var tag = _db.Tags.FindById(Tag.Key(Context.Guild.Id, name));
            if (tag is null) { await RespondAsync(embed: BotEmbeds.Error($"No tag named `{name}`."), ephemeral: true); return; }
            tag.Uses++;
            _db.Tags.Upsert(tag);
            await RespondAsync(tag.Content);
        }

        [SlashCommand("create", "Create or update a tag.")]
        [RequireLevel(PermissionLevel.Moderator)]
        public async Task Create(string name, string content)
        {
            name = name.Trim().ToLowerInvariant();
            _db.Tags.Upsert(new Tag { Id = Tag.Key(Context.Guild.Id, name), GuildId = Context.Guild.Id, Name = name, Content = content.Replace("\\n", "\n"), AuthorId = Context.User.Id });
            await RespondAsync(embed: BotEmbeds.Ok($"Saved tag `{name}`."), ephemeral: true);
        }

        [SlashCommand("delete", "Delete a tag.")]
        [RequireLevel(PermissionLevel.Moderator)]
        public async Task Delete(string name)
        {
            var ok = _db.Tags.Delete(Tag.Key(Context.Guild.Id, name.Trim().ToLowerInvariant()));
            await RespondAsync(embed: ok ? BotEmbeds.Ok($"Deleted tag `{name}`.") : BotEmbeds.Error($"No tag `{name}`."), ephemeral: true);
        }

        [SlashCommand("list", "List all tags.")]
        public async Task List()
        {
            var tags = _db.Tags.Find(t => t.GuildId == Context.Guild.Id).OrderByDescending(t => t.Uses).Take(30).ToList();
            if (tags.Count == 0) { await RespondAsync(embed: BotEmbeds.Info("No tags yet.")); return; }
            await RespondAsync(embed: BotEmbeds.Base("Tags", string.Join(", ", tags.Select(t => $"`{t.Name}`"))).Build());
        }
    }

    [Group("giveaway", "Run giveaways.")]
    [RequireLevel(PermissionLevel.Moderator)]
    public sealed class GiveawayModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly BotDatabase _db;
        private readonly GiveawayService _service;
        public GiveawayModule(BotDatabase db, GiveawayService service) { _db = db; _service = service; }

        [SlashCommand("start", "Start a giveaway.")]
        public async Task Start(string prize, [Summary(description: "Examples: 1h, 2d")] string duration, int winners = 1)
        {
            var span = DurationParser.Parse(duration);
            if (span is null) { await RespondAsync(embed: BotEmbeds.Error("Bad duration."), ephemeral: true); return; }
            var endsAt = DateTime.UtcNow + span.Value;
            var embed = BotEmbeds.Base($"🎉 {prize}")
                .WithDescription($"React with 🎉 to enter!\nWinners: **{winners}**\nEnds {TimestampTag.FromDateTimeOffset(new DateTimeOffset(endsAt, TimeSpan.Zero), TimestampTagStyles.Relative)}")
                .WithFooter($"Hosted by {Context.User.Username}").Build();
            await RespondAsync(embed: embed);
            var message = await GetOriginalResponseAsync();
            await message.AddReactionAsync(new Emoji("🎉"));
            _db.Giveaways.Insert(new Giveaway
            {
                GuildId = Context.Guild.Id, ChannelId = Context.Channel.Id, MessageId = message.Id,
                HostId = Context.User.Id, Prize = prize, Winners = Math.Max(1, winners), EndsAt = endsAt,
            });
        }

        [SlashCommand("end", "End a giveaway now by message ID.")]
        public async Task End(string messageId)
        {
            if (!ulong.TryParse(messageId, out var id)) { await RespondAsync(embed: BotEmbeds.Error("Bad message ID."), ephemeral: true); return; }
            var giveaway = _db.Giveaways.FindOne(g => g.MessageId == id);
            if (giveaway is null) { await RespondAsync(embed: BotEmbeds.Error("No giveaway with that message ID."), ephemeral: true); return; }
            await _service.Conclude(giveaway);
            await RespondAsync(embed: BotEmbeds.Ok("Giveaway ended."), ephemeral: true);
        }

        [SlashCommand("reroll", "Reroll a finished giveaway.")]
        public async Task Reroll(string messageId)
        {
            if (!ulong.TryParse(messageId, out var id)) { await RespondAsync(embed: BotEmbeds.Error("Bad message ID."), ephemeral: true); return; }
            var giveaway = _db.Giveaways.FindOne(g => g.MessageId == id);
            if (giveaway is null) { await RespondAsync(embed: BotEmbeds.Error("No giveaway with that message ID."), ephemeral: true); return; }
            giveaway.Ended = false;
            await _service.Conclude(giveaway);
            await RespondAsync(embed: BotEmbeds.Ok("Rerolled."), ephemeral: true);
        }
    }

    [Group("reactionrole", "Self-assign roles by reaction.")]
    [RequireLevel(PermissionLevel.Admin)]
    public sealed class ReactionRoleModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly BotDatabase _db;
        public ReactionRoleModule(BotDatabase db) => _db = db;

        [SlashCommand("add", "Bind an emoji reaction on a message to a role.")]
        public async Task Add(string messageId, string emoji, SocketRole role)
        {
            if (!ulong.TryParse(messageId, out var id)) { await RespondAsync(embed: BotEmbeds.Error("Bad message ID."), ephemeral: true); return; }
            if (await Context.Channel.GetMessageAsync(id) is not IUserMessage message)
            {
                await RespondAsync(embed: BotEmbeds.Error("Message not found in this channel."), ephemeral: true); return;
            }
            IEmote emote = Emote.TryParse(emoji, out var custom) ? custom : new Emoji(emoji);
            _db.ReactionRoles.Upsert(new ReactionRole { Id = ReactionRole.Key(id, emote.ToString()!), GuildId = Context.Guild.Id, MessageId = id, Emoji = emote.ToString()!, RoleId = role.Id });
            await message.AddReactionAsync(emote);
            await RespondAsync(embed: BotEmbeds.Ok($"{emoji} on that message now grants {role.Mention}."), ephemeral: true);
        }

        [SlashCommand("remove", "Remove a reaction-role binding.")]
        public async Task Remove(string messageId, string emoji)
        {
            if (!ulong.TryParse(messageId, out var id)) { await RespondAsync(embed: BotEmbeds.Error("Bad message ID."), ephemeral: true); return; }
            IEmote emote = Emote.TryParse(emoji, out var custom) ? custom : new Emoji(emoji);
            var ok = _db.ReactionRoles.Delete(ReactionRole.Key(id, emote.ToString()!));
            await RespondAsync(embed: ok ? BotEmbeds.Ok("Removed.") : BotEmbeds.Error("No such binding."), ephemeral: true);
        }
    }

    [Group("ticket", "Support tickets.")]
    public sealed class TicketModule : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly BotDatabase _db;
        public TicketModule(BotDatabase db) => _db = db;

        [SlashCommand("open", "Open a support ticket.")]
        public async Task Open(string reason = "No reason given.")
        {
            var config = _db.GetGuild(Context.Guild.Id);
            if (config.TicketCategoryId is not { } categoryId)
            {
                await RespondAsync(embed: BotEmbeds.Error("Tickets aren't set up. Ask an admin to run `/config tickets`."), ephemeral: true);
                return;
            }
            await DeferAsync(ephemeral: true);
            var channel = await Context.Guild.CreateTextChannelAsync($"ticket-{Context.User.Username}", p => p.CategoryId = categoryId);
            await channel.AddPermissionOverwriteAsync(Context.Guild.EveryoneRole, new OverwritePermissions(viewChannel: PermValue.Deny));
            await channel.AddPermissionOverwriteAsync(Context.User, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));
            if (config.SupportRoleId is { } supportId && Context.Guild.GetRole(supportId) is { } supportRole)
                await channel.AddPermissionOverwriteAsync(supportRole, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow));

            var ping = config.SupportRoleId is { } sid ? $"<@&{sid}>" : "";
            await channel.SendMessageAsync(ping, embed: BotEmbeds.Base($"Ticket from {Context.User.Username}", reason)
                .WithFooter("Staff: use /ticket close here when resolved.").Build());
            await FollowupAsync(embed: BotEmbeds.Ok($"Opened {channel.Mention}."), ephemeral: true);
        }

        [SlashCommand("close", "Close the current ticket channel.")]
        [RequireLevel(PermissionLevel.Moderator)]
        public async Task Close()
        {
            if (Context.Channel is not ITextChannel channel || !channel.Name.StartsWith("ticket-"))
            {
                await RespondAsync(embed: BotEmbeds.Error("Run this inside a ticket channel."), ephemeral: true);
                return;
            }
            await RespondAsync(embed: BotEmbeds.Ok("Closing this ticket in 5s…"));
            await Task.Delay(5000);
            await channel.DeleteAsync();
        }
    }
}
