YTMT-STUDIO — WINDOWS DESKTOP BUNDLE

1) Open YTMT-STUDIO-Windows\YTMT-STUDIO.exe.
2) On first launch, enter your Discord Application / Client ID and click "Discord ile Giriş Yap".
3) Approve the Discord OAuth2 request in your browser. The app does not need a YTMT Studio username/password or the Render AuthService.
4) The exact loopback redirect must be registered in Discord Developer Portal:
   http://127.0.0.1:53682/callback
5) Enable the Discord application's Public OAuth2 Client option for the PKCE desktop flow.

IMPORTANT
- This bundle does not contain Discord Client Secret, bot token, MongoDB URI, or GitHub token.
- Discord login uses Authorization Code + PKCE directly with Discord.
- Discord tokens are protected with Windows DPAPI CurrentUser.
- The Client ID is public and is saved in the local Studio settings file.
- Server settings are saved as local drafts first. Export JSON and run the documented YTMT-BOT import command to apply settings to the bot database.
- See Bot-Integration\README.md for the full integration and import guide.
- The published app requires the .NET 10 Windows Desktop Runtime.

SOURCE
The YTMT-STUDIO-Source folder contains the full source project. Bot-Integration contains the exact YTMT-BOT integration overlay files.
