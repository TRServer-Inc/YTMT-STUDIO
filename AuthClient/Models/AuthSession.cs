namespace YTMT_STUDIO.AuthClient.Models;

public sealed class AuthSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;

    // Access token: 15 minutes. Refresh session: server-controlled lifetime.
    public DateTimeOffset AccessExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public AuthUser User { get; set; } = new();

    // Only remembered sessions are written to the DPAPI-protected file.
    public bool RememberMe { get; set; } = true;
}