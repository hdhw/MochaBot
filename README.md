# Mocha Bot (C# / .NET 8)

A ground-up rewrite of the Mocha multi-utility Discord bot in C# on .NET 8, using Discord.Net and LiteDB. Replaces the old Node bot.

## Run

```bash
cp .env.example .env   # fill in DISCORD_TOKEN, CLIENT_ID, GUILD_ID, OWNER_IDS
dotnet run             # or: dotnet build -c Release && ./bin/Release/net8.0/MochaBot
```

Commands register to `GUILD_ID` instantly, or globally if it's blank. Data lives in `data/mocha-bot.db` (LiteDB) next to the binary.

## Features

- **Moderation** — kick, ban, unban, timeout, untimeout, warn/warnings/removewarning/clearwarnings, purge, slowmode, lock, unlock, nickname, plus a case-numbered modlog (`case`, `cases`) posted to the log channel.
- **Admin** — one `/config` command: logs, welcome, goodbye, autorole, suggestions, tickets, starboard, level-up channel, level rewards, mod/admin roles, and automod (invites/links/caps/spam + word filter).
- **Mocha / RecNet** — `player`, `room`, `status`, `events`, `photos`, reading the server's public RecNet JSON APIs. A background task keeps the status + online-players channels updated.
- **Engagement** — message XP with levels, level-up role rewards, `rank`, `leaderboard`, and a coin economy (`daily`, `balance`, `pay`).
- **Utility** — `poll`, `giveaway` (start/end/reroll), `reactionrole`, `embed`, `tag`, `suggest`, `ticket`, `remind`/`reminders`, starboard.
- **Mocha admin bridge** — link a Discord user to a Mocha account (`/link`, `/unlink`, `/whoami`), then linked admins can run ephemeral admin actions like `/edit-profile uid:1 display:"Coach"`. Requires `BOT_ADMIN_SECRET` matching the server, and a matching `recnet/api/bot/*` bridge on the Mocha server.

## Permissions

Tiers resolve from Discord: server owner / `OWNER_IDS` = owner; Administrator or configured admin roles = admin; ModerateMembers/Kick/Ban or configured mod roles = moderator. Set extra roles with `/config role`.
