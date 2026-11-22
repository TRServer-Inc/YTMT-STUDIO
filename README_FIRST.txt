YTMT-STUDIO — WINDOWS DESKTOP BUNDLE

1) Open YTMT-STUDIO-Windows\YTMT-STUDIO.exe.
2) The app uses the existing YTMT Studio Authentication API configured in auth.config.json.
3) Open Sunucu Yönetimi. Save your Discord Application / Client ID, then connect Discord.
4) The exact loopback redirect must be registered in Discord Developer Portal:
   http://127.0.0.1:53682/callback
5) Enable the Discord application's Public OAuth2 Client option for the PKCE desktop flow.

IMPORTANT
- This bundle does not contain Discord Client Secret, bot token, MongoDB URI, or GitHub token.
- Discord tokens are protected with Windows DPAPI CurrentUser.
- Server settings are saved as local drafts first. Export JSON and run the documented YTMT-BOT import command to apply settings to the bot database.
- See BOT-INTEGRATION\README.md for the full integration and import guide.
- The published app requires the .NET 10 Windows Desktop Runtime.

SOURCE
The YTMT-STUDIO folder contains the full source project. Bot-Integration contains the exact YTMT-BOT integration overlay files.
