# YTMT-STUDIO 1.5 — 17.11.2026

## Added
- New owner-only Bot Yönetimi page.
- Render service status lookup.
- Resume, suspend, and restart actions with confirmation prompts.
- Manual refresh of the latest 50 Render application log entries.
- Windows DPAPI storage for the Render API key.
- Service ID setting persisted in `settings.json`.

## Safety / behavior notes
- Render suspend affects the entire selected Render service, including its HTTP/API endpoints; it is not a Discord-client-only stop.
- Render API key is not an OpenAI/GPT API key and is not stored in source files or `settings.json`.
- Render control UI is restricted to username `YTMTDev` because the current AuthService user object does not expose a role claim.
- Logs are fetched on demand, not streamed live.

## Validation
- Source reviewed against stored YTMT-STUDIO v1.4 project.
- Build not run: .NET SDK/compiler unavailable in the generation environment.