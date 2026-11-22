namespace YTMT_STUDIO.Models;

/// <summary>
/// A local, exportable draft of the settings for one Discord server.
/// Saving a workspace does not silently change the live bot.
/// </summary>
public sealed class GuildWorkspace
{
    public string GuildId { get; set; } = string.Empty;
    public string GuildName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<string> AutoRoleIds { get; set; } = [];
    public bool WelcomeEnabled { get; set; }
    public string WelcomeChannelId { get; set; } = string.Empty;
    public string WelcomeMessage { get; set; } = "Hoş geldin {user}! Sunucumuza katıldın.";
    public bool AntiSpamEnabled { get; set; }
    public bool CapsFilterEnabled { get; set; } = true;
    public bool ProfanityFilterEnabled { get; set; } = true;
    public string LogChannelId { get; set; } = string.Empty;
    public List<ReactionRoleDraft> ReactionRoles { get; set; } = [];
}

public sealed class ReactionRoleDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>existing-message or new-message</summary>
    public string Mode { get; set; } = "existing-message";
    public string ChannelId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string MessageContent { get; set; } = string.Empty;
    public List<ReactionRoleMappingDraft> Mappings { get; set; } = [];
}

public sealed class ReactionRoleMappingDraft
{
    public string Emoji { get; set; } = "✅";
    public string RoleId { get; set; } = string.Empty;
}
