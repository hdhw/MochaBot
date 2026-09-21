using Discord;

namespace MochaBot.Utils;

public static class BotEmbeds
{
    public static readonly Color Brand = new(0xBF, 0x5A, 0xF2);
    public static readonly Color Success = new(0x43, 0xB5, 0x81);
    public static readonly Color Danger = new(0xE5, 0x3E, 0x3E);
    public static readonly Color Warn = new(0xE0, 0x9E, 0x2A);

    public static EmbedBuilder Base(string? title = null, string? description = null) =>
        new EmbedBuilder()
            .WithColor(Brand)
            .WithTitle(title)
            .WithDescription(description);

    public static Embed Ok(string message, string? title = null) =>
        new EmbedBuilder().WithColor(Success).WithTitle(title).WithDescription(message).Build();

    public static Embed Error(string message, string? title = null) =>
        new EmbedBuilder().WithColor(Danger).WithTitle(title ?? "Something went wrong").WithDescription(message).Build();

    public static Embed Info(string message, string? title = null) =>
        new EmbedBuilder().WithColor(Brand).WithTitle(title).WithDescription(message).Build();
}
