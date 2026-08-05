# AlphaChannel

A Dalamud plugin that renders a real, movable video screen in the world and keeps it in sync
across everyone watching, so you and your friends can watch something together in-game.

Not a mod-sync overlay and not a screen-share - the video actually plays for everyone
independently, driven by a small self-hosted relay server that just passes playback state (URL,
position, pause/play, screen position) between clients.

## Install

Add this as a custom plugin repository in-game:

1. `/xlsettings` -> Experimental -> Custom Plugin Repositories
2. Paste: `https://raw.githubusercontent.com/vinney491-dotcom/AlphaChannel/master/pluginmaster.json`
3. Add, then find **AlphaChannel** under Available Plugins in the normal plugin installer and
   install it like any other plugin.

No account, no sign-up - the plugin generates its own local identity on first run and connects to
the relay automatically. The first time you load it on a given character, you'll be asked to pick
a display name (defaults to your character name).

## Using it

- `/achannel` opens the window.
- Paste a video URL (or search YouTube right from the window) and hit Play now.
- Drag the screen into position with the Screen controls, save it as a named preset if you want to
  reuse that spot later.
- **Watch-along**: right-click a friend who's also running AlphaChannel and choose *Join Stream*,
  or type their in-plugin display name under Watch-along -> Join. Whoever's hosting can hand
  control to a viewer at any time ("Make host" next to their name in the roster), and can copy a
  ready-made party-chat invite message with one click.
- The video auto-pauses if the host enters combat or a cutscene, and resumes after.
- Send quick emoji reactions during a watch-along from the Reactions row.

## Features

- In-world video screen (YouTube and most direct video URLs), with volume, seek, and a queue with
  thumbnails you can reorder
- In-plugin YouTube search
- Real-time watch-along sync over a self-hosted relay - join by name, no accounts
- See who's watching, and hand off hosting to another viewer
- Auto-pause during combat/cutscenes
- Emoji reactions
- Saveable screen position presets

## Self-hosting the relay

The plugin talks to a small ASP.NET Core relay (`src/AlphaChannel.Server`) over WebSockets. To run
your own instead of relying on someone else's:

```
cp .env.example .env   # set RELAY_DOMAIN to a domain pointed at your server, and a random ADMIN_TOKEN
docker compose up -d --build
```

This brings up the relay behind Caddy, which handles TLS automatically for `RELAY_DOMAIN`. The
plugin's default relay address is set in `Configuration.cs` (`RelayServerUrl`) - point your own
build at your own relay by changing that before building, or add a settings UI for it if you want
that configurable at runtime (v1 deliberately hides this from players so they don't need to know
or type a server address).

The relay has no real authentication - see the code comments in `UserDirectory.cs` and
`ConnectionHandler.cs` for the details. Fine for a small trusted group; not meant to be a
public-internet-safe auth story as-is.

## Building

Requires the .NET 10 SDK and a Dalamud dev environment (`DALAMUD_HOME` set, or the default XIVLauncher
install location).

```
dotnet build AlphaChannel.slnx
```

`src/AlphaChannel.Plugin` is the Dalamud plugin, `src/AlphaChannel.Server` is the relay,
`src/AlphaChannel.Contracts` is the shared wire-format library both depend on.
