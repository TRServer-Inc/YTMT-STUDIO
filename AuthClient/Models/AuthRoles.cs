namespace YTMT_STUDIO.AuthClient.Models;

/// <summary>
/// Central role names and UI permissions used by YTMT-STUDIO.
/// The AuthService is the source of truth for each account's role.
/// </summary>
public static class AuthRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string User = "user";

    public static string Normalize(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            Owner => Owner,
            Admin => Admin,
            User => User,
            _ => User
        };
    }

    public static bool CanManageRender(string? role) =>
        Normalize(role) == Owner;

    public static bool CanAccessPage(string? role, string pageKey)
    {
        string normalizedRole = Normalize(role);
        string key = pageKey.Trim().ToLowerInvariant();

        return normalizedRole switch
        {
            Owner => key is "dashboard" or "botcontrol" or "terminal" or "commands" or "monitor" or "settings",
            Admin => key is "dashboard" or "terminal" or "commands" or "monitor" or "settings",
            User => key is "dashboard" or "commands" or "monitor",
            _ => key == "dashboard"
        };
    }

    public static string GetDisplayName(string? role) => Normalize(role) switch
    {
        Owner => "Sahip",
        Admin => "Yönetici",
        _ => "Kullanıcı"
    };
}