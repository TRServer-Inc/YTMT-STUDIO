# YTMT-STUDIO 1.5 — Render bot control setup

This update adds a Render service-control page to the existing YTMT-STUDIO v1.4 WinForms project.

## What it controls

- **Başlat:** resumes a suspended Render service (`POST /v1/services/{serviceId}/resume`).
- **Durdur:** suspends the Render service (`POST /v1/services/{serviceId}/suspend`).
- **Yeniden Başlat:** restarts the Render service (`POST /v1/services/{serviceId}/restart`).
- **Durumu Yenile:** reads the service record (`GET /v1/services/{serviceId}`).
- **Logları Yenile:** retrieves the latest 50 application log entries (`GET /v1/logs`, filtered by owner and service).

These are Render infrastructure actions, not just Discord.js `client.destroy()` / `client.login()` actions. Suspending the service also makes the bot's HTTP/API endpoints in that same Render service unavailable. Keep this in mind before using **Durdur**.

## Required Render values

1. Open the YTMT BOT service in the Render Dashboard and copy its service ID. It starts with `srv-`.
2. Create a Render API key from Render Account Settings. If scoped API keys are available for the account, restrict the key to the YTMT BOT service and only the read / log / restart / suspend / resume permissions needed by this screen.
3. Do not send the key in chat, add it to source code, or commit it to GitHub. The app stores the key in the current Windows user's Local AppData using Windows DPAPI encryption. The key is not stored in `settings.json`.
4. Launch YTMT-STUDIO with the `YTMTDev` account, open **Bot Yönetimi**, enter the `srv-...` service ID and API key, then click **Ayarları Kaydet**.
5. Click **Durumu Yenile**. Once the status is retrieved, the applicable buttons become available.
6. Click **Logları Yenile** when you want to inspect the latest application logs. This is a manual recent-log query, not a live tail.

Render API keys are infrastructure credentials, not OpenAI/GPT API keys. They do not create AI usage charges, but they grant the permissions assigned to that Render key. Render's standard API key provides access to the workspaces the account belongs to; only use a scoped key when the dashboard offers it, and keep the key private.

## Files in this patch

### Replace

- `MainForm.cs`
- `Models/AppSettings.cs`

### Add

- `MainForm.BotControl.cs`
- `Services/RenderControlService.cs`
- `Services/RenderCredentialStore.cs`

No change is needed to `Program.cs`, the existing login/authentication service, `auth.config.json`, or `MainForm.Monitoring.cs`.

## Access control

The current Auth API does not return a role claim to the desktop application. To avoid exposing Render infrastructure controls to every Studio account, this patch shows the Bot Yönetimi page only to the existing owner username `YTMTDev`. This is a desktop UI restriction; a future server-side control proxy should enforce authorization independently if additional users are added.

## Build validation

The patch was reviewed against the stored v1.4 source. The generation environment did not have the .NET SDK/compiler available, so a Windows .NET 10 build must still be confirmed with **Build > Rebuild Solution** in Visual Studio 2026.

## Official Render API references

- Authentication: https://api-docs.render.com/reference/authentication
- Retrieve service: https://api-docs.render.com/reference/retrieve-service
- Resume service: https://api-docs.render.com/reference/resume-service-1
- Suspend service: https://api-docs.render.com/reference/suspend-service-1
- Restart service: https://api-docs.render.com/reference/restart-service
- List logs: https://api-docs.render.com/reference/list-logs