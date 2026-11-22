namespace YTMT_STUDIO.AuthClient.Models;

public sealed class AuthUser
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = AuthRoles.User;

    public bool CanManageRender => AuthRoles.CanManageRender(Role);
}