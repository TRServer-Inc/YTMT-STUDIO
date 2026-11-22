using System.Text.Json;
using YTMT_STUDIO.Models;

namespace YTMT_STUDIO.Services;

/// <summary>
/// Stores local, per-user server configuration drafts and a small audit trail.
/// No Discord access token or bot secret is written by this service.
/// </summary>
public sealed class GuildWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YTMT-STUDIO", "GuildWorkspaces");

    public GuildWorkspace Load(string guildId, string guildName)
    {
        ValidateGuildId(guildId);
        string path = GetWorkspacePath(guildId);

        try
        {
            if (File.Exists(path))
            {
                var workspace = JsonSerializer.Deserialize<GuildWorkspace>(
                    File.ReadAllText(path), JsonOptions);
                if (workspace is not null &&
                    string.Equals(workspace.GuildId, guildId, StringComparison.Ordinal))
                {
                    workspace.ReactionRoles ??= [];
                    workspace.AutoRoleIds ??= [];
                    return workspace;
                }
            }
        }
        catch (JsonException)
        {
            AppendAudit(guildId, guildName, "workspace_load_failed", "Taslak JSON okunamadı.");
        }
        catch (IOException)
        {
            // Return a fresh workspace; a later save can repair the local copy.
        }

        return new GuildWorkspace { GuildId = guildId, GuildName = guildName };
    }

    public void Save(GuildWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateGuildId(workspace.GuildId);
        Directory.CreateDirectory(_root);
        workspace.UpdatedAtUtc = DateTimeOffset.UtcNow;
        string path = GetWorkspacePath(workspace.GuildId);
        string temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(workspace, JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        AppendAudit(workspace.GuildId, workspace.GuildName, "workspace_saved",
            "Yerel sunucu taslağı kaydedildi.");
    }

    public void Export(GuildWorkspace workspace, string path)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateGuildId(workspace.GuildId);
        File.WriteAllText(path, JsonSerializer.Serialize(workspace, JsonOptions));
    }

    public GuildWorkspace Import(string path, string expectedGuildId)
    {
        ValidateGuildId(expectedGuildId);
        var workspace = JsonSerializer.Deserialize<GuildWorkspace>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("JSON dosyasında geçerli bir sunucu taslağı bulunamadı.");
        ValidateGuildId(workspace.GuildId);
        if (!string.Equals(workspace.GuildId, expectedGuildId, StringComparison.Ordinal))
            throw new InvalidDataException("Bu taslak seçili sunucuya ait değil.");
        workspace.AutoRoleIds ??= [];
        workspace.ReactionRoles ??= [];
        return workspace;
    }

    public void AppendAudit(string guildId, string guildName, string action, string detail)
    {
        try
        {
            ValidateGuildId(guildId);
            Directory.CreateDirectory(_root);
            string path = Path.Combine(_root, "audit.jsonl");
            var entry = new
            {
                atUtc = DateTimeOffset.UtcNow,
                guildId,
                guildName = guildName ?? string.Empty,
                action,
                detail = detail ?? string.Empty
            };
            File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
            // Audit logging must never prevent a settings workflow from opening.
        }
    }

    public string AuditLogPath
    {
        get
        {
            Directory.CreateDirectory(_root);
            return Path.Combine(_root, "audit.jsonl");
        }
    }

    private string GetWorkspacePath(string guildId) => Path.Combine(_root, guildId + ".json");

    private static void ValidateGuildId(string guildId)
    {
        if (string.IsNullOrWhiteSpace(guildId) ||
            guildId.Length > 24 ||
            guildId.Any(ch => !char.IsAsciiDigit(ch)))
            throw new ArgumentException("Geçerli bir Discord sunucu kimliği gerekli.", nameof(guildId));
    }
}
