# YTMT-STUDIO — Discord Login Edition

.NET 10 Windows Forms desktop application for YTMT BOT management.

## Sign-in

- Discord is the only sign-in provider.
- The desktop app uses Discord OAuth2 Authorization Code + PKCE directly.
- No Studio username/password account is required.
- No Render AuthService, database, or Auth API is required to sign in.
- On first launch, enter the Discord Application Client ID. It is stored in local app settings.
- Discord access and refresh tokens are protected with Windows DPAPI CurrentUser.

## Discord Developer Portal setup

1. Add this exact OAuth2 redirect URI: `http://127.0.0.1:53682/callback`.
2. Enable the application's Public OAuth2 Client option for the PKCE desktop flow.
3. The app requests only `identify` and `guilds` scopes.
4. Do not add a Client Secret to the desktop app. It is not used.

## Features

- Discord account sign-in and remembered local session.
- Manageable Discord guild listing based on the current account's permissions.
- YTMT BOT installation status and invite link.
- Multiple auto-role drafts.
- Reaction-role drafts for existing messages or new bot messages.
- Welcome, log-channel, and moderation setting drafts.
- Local JSON import/export, validation, and audit log.
- Existing dashboard, WebView2 terminal, command catalog, API monitoring, and live event stream.

## Bot configuration

Server settings are saved locally as drafts under:

`%LOCALAPPDATA%\YTMT-STUDIO\GuildWorkspaces\`

Export the selected guild's JSON file and import it from the YTMT-BOT project folder using:

```bash
npm run import-studio-config -- "C:\path\to\ytmt-guild-123456789012345678.json"
```

The bot integration guide is in `Bot-Integration/README.md`.

## Build

- Target: .NET 10 Windows Forms, Windows x64.
- Published app: `YTMT-STUDIO-Windows/`.
- Source: `YTMT-STUDIO-Source/`.
- The published app is framework-dependent and requires the .NET 10 Windows Desktop Runtime.
