# Project Analysis — YTMT-STUDIO v1.4 Auth

## Existing application

- .NET 10 Windows Forms desktop application.
- Existing entry point: `Program.cs`.
- Existing main window: `MainForm.cs` and partial `MainForm.Monitoring.cs`.
- Existing bot API client: `Services/ApiService.cs`.
- Existing monitoring API endpoints: `/api/stats` and `/api/events/stream`.

## Authentication integration

The application startup path is now:

1. Initialize WinForms.
2. Read the DPAPI-protected session file.
3. Attempt refresh-token validation against Render.
4. If no valid session is available, show `AuthClient/Forms/LoginForm.cs`.
5. On success, open `MainForm` and show the authenticated account in the sidebar.

The login module is isolated under `AuthClient/` and does not alter the existing bot API client.

## Backend

`AuthService/` is a standalone Node.js service. It reads a JSON account list from a separate private GitHub repository through a Render-only token and stores hashed opaque token identifiers in PostgreSQL.

Account records have one permission level; there is no role-management or public registration system.

## Session model

- Access token lifetime: 15 minutes.
- Refresh session absolute lifetime: 30 days when Remember Me is selected; 12 hours otherwise.
- Refresh does not extend absolute expiry.
- Windows refresh token storage: DPAPI CurrentUser.
- Auth heartbeat: 2 minutes.

## Deployment dependencies

- Render Web Service (Node.js 22+)
- Render PostgreSQL
- Private GitHub account repository
- GitHub fine-grained token with read-only Contents permission

## Security boundary

The change gates opening the Windows Forms application. It does not retrofit authentication into the existing public bot monitoring endpoints.