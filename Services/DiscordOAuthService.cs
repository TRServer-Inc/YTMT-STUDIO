using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using YTMT_STUDIO.Models;

namespace YTMT_STUDIO.Services;

/// <summary>
/// Discord desktop OAuth2 client using Authorization Code + PKCE.
/// Requires the Discord application's Public OAuth2 Client option and the exact
/// loopback redirect URI http://127.0.0.1:53682/callback.
/// The client secret is intentionally never requested or stored.
/// </summary>
public sealed class DiscordOAuthService : IDisposable
{
    public const string RedirectUri = "http://127.0.0.1:53682/callback";
    private const string DiscordApi = "https://discord.com/api/v10/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _clientId;
    private readonly string _botApiBaseUrl;
    private readonly string _sessionPath;
    private bool _disposed;

    public DiscordOAuthService(string clientId, string botApiBaseUrl)
    {
        _clientId = clientId?.Trim() ?? string.Empty;
        _botApiBaseUrl = NormalizeBaseUrl(botApiBaseUrl);
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YTMT-STUDIO");
        Directory.CreateDirectory(folder);
        _sessionPath = Path.Combine(folder, "discord-session.bin");
    }

    public async Task<DiscordSession> SignInAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(_clientId) ||
            !ulong.TryParse(_clientId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new InvalidOperationException("Discord Application / Client ID alanına sayısal Client ID gir.");
        }

        string state = Base64Url(RandomNumberGenerator.GetBytes(32));
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        using var listener = new TcpListener(IPAddress.Loopback, 53682);
        listener.Start(backlog: 1);

        string authorizeUrl = "https://discord.com/oauth2/authorize" +
            "?client_id=" + Uri.EscapeDataString(_clientId) +
            "&response_type=code" +
            "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
            "&scope=" + Uri.EscapeDataString("identify guilds") +
            "&state=" + Uri.EscapeDataString(state) +
            "&code_challenge=" + Uri.EscapeDataString(challenge) +
            "&code_challenge_method=S256" +
            "&prompt=consent";

        try
        {
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            listener.Stop();
            throw new InvalidOperationException("Varsayılan tarayıcı açılamadı: " + ex.Message, ex);
        }

        string? code;
        using (var callbackTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            callbackTimeout.CancelAfter(TimeSpan.FromMinutes(4));
            try
            {
                using TcpClient callbackClient = await listener.AcceptTcpClientAsync()
                    .WaitAsync(callbackTimeout.Token);
                await using NetworkStream stream = callbackClient.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);

                string? requestLine = await reader.ReadLineAsync().WaitAsync(callbackTimeout.Token);
                string[] requestParts = requestLine?.Split(' ', 3) ?? [];
                Uri? callbackUri = requestParts.Length == 3 &&
                    string.Equals(requestParts[0], "GET", StringComparison.Ordinal)
                    ? new Uri(new Uri("http://127.0.0.1:53682/"), requestParts[1])
                    : null;

                int headerBytes = 0;
                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync().WaitAsync(callbackTimeout.Token)))
                {
                    headerBytes += headerLine.Length;
                    if (headerBytes > 16_384) break;
                }

                Dictionary<string, string> query = callbackUri is null
                    ? new(StringComparer.Ordinal)
                    : ParseQuery(callbackUri.Query);
                query.TryGetValue("state", out string? callbackState);
                query.TryGetValue("code", out code);
                query.TryGetValue("iss", out string? issuer);
                query.TryGetValue("error", out string? error);

                bool validPath = string.Equals(
                    callbackUri?.AbsolutePath.TrimEnd('/'),
                    "/callback",
                    StringComparison.Ordinal);

                if (error is not null || !validPath ||
                    !string.Equals(state, callbackState, StringComparison.Ordinal) ||
                    (issuer is not null && !string.Equals(issuer, "https://discord.com", StringComparison.Ordinal)) ||
                    string.IsNullOrWhiteSpace(code))
                {
                    await WriteCallbackPageAsync(stream, false, cancellationToken);
                    throw new InvalidOperationException(error == "access_denied"
                        ? "Discord yetkilendirmesi iptal edildi."
                        : "Discord OAuth2 dönüşü doğrulanamadı. State, issuer veya callback adresi eşleşmedi.");
                }

                await WriteCallbackPageAsync(stream, true, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Discord giriş penceresi zaman aşımına uğradı. Tekrar deneyebilirsin.");
            }
        }

        DiscordSession session = await ExchangeCodeAsync(code, verifier, cancellationToken);
        DiscordProfile profile = await GetProfileAsync(session.AccessToken, cancellationToken);
        session.UserId = profile.Id;
        session.Username = profile.Username;
        session.GlobalName = profile.GlobalName;
        session.Avatar = profile.Avatar;
        SaveSession(session);
        return session;
    }

    public async Task<DiscordSession?> LoadSavedSessionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!File.Exists(_sessionPath)) return null;

        try
        {
            byte[] encrypted = await File.ReadAllBytesAsync(_sessionPath, cancellationToken);
            byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<DiscordSession>(clear, JsonOptions);
            CryptographicOperations.ZeroMemory(clear);
            if (session is null || string.IsNullOrWhiteSpace(session.RefreshToken))
                return null;

            if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
            {
                session = await RefreshSessionAsync(session, cancellationToken);
                SaveSession(session);
            }

            if (string.IsNullOrWhiteSpace(session.UserId))
            {
                DiscordProfile profile = await GetProfileAsync(session.AccessToken, cancellationToken);
                session.UserId = profile.Id;
                session.Username = profile.Username;
                session.GlobalName = profile.GlobalName;
                session.Avatar = profile.Avatar;
                SaveSession(session);
            }

            return session;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or HttpRequestException or InvalidOperationException)
        {
            ClearSavedSession();
            return null;
        }
    }

    public async Task<List<DiscordGuildInfo>> GetManageableGuildsAsync(
        DiscordSession session,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(DiscordApi), "users/@me/guilds?with_counts=true"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Discord sunucu listesi alınamadı (HTTP {(int)response.StatusCode}).");

        List<DiscordGuildInfo> all = JsonSerializer.Deserialize<List<DiscordGuildInfo>>(body, JsonOptions) ?? [];
        return all
            .Where(g => g.Owner || g.IsAdministrator || g.CanManageGuild || g.CanManageRoles)
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<DiscordGuildBotStatus> GetBotStatusAsync(
        string guildId,
        string discordAccessToken,
        CancellationToken cancellationToken = default)
    {
        string url = _botApiBaseUrl + "api/studio/guilds/" + Uri.EscapeDataString(guildId) + "/status";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", discordAccessToken);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new DiscordGuildBotStatus { Installed = false, ServiceAvailable = true };
        if (!response.IsSuccessStatusCode)
            return new DiscordGuildBotStatus
            {
                Installed = false,
                ServiceAvailable = false,
                Error = $"YTMT BOT API HTTP {(int)response.StatusCode}"
            };

        return await response.Content.ReadFromJsonAsync<DiscordGuildBotStatus>(JsonOptions, cancellationToken)
            ?? new DiscordGuildBotStatus { Installed = false, ServiceAvailable = false, Error = "Boş API yanıtı." };
    }

    public async Task<DiscordGuildResources> GetGuildResourcesAsync(
        string guildId,
        string discordAccessToken,
        CancellationToken cancellationToken = default)
    {
        string url = _botApiBaseUrl + "api/studio/guilds/" + Uri.EscapeDataString(guildId) + "/resources";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", discordAccessToken);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sunucu kaynakları alınamadı (HTTP {(int)response.StatusCode}).");
        return await response.Content.ReadFromJsonAsync<DiscordGuildResources>(JsonOptions, cancellationToken)
            ?? new DiscordGuildResources();
    }

    public void SaveSession(DiscordSession session)
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        byte[] protectedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(clear);
        string temp = _sessionPath + ".tmp";
        try
        {
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _sessionPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void ClearSavedSession()
    {
        try
        {
            if (File.Exists(_sessionPath)) File.Delete(_sessionPath);
        }
        catch (IOException) { }
    }

    public static string BuildBotInviteUrl(string clientId, string guildId)
    {
        const ulong permissions =
            (1UL << 28) | // Manage Roles
            (1UL << 13) | // Manage Messages
            (1UL << 6)  | // Add Reactions
            (1UL << 16) | // Read Message History
            (1UL << 11) | // Send Messages
            (1UL << 10);  // View Channels

        return "https://discord.com/oauth2/authorize" +
            "?client_id=" + Uri.EscapeDataString(clientId) +
            "&scope=bot%20applications.commands" +
            "&permissions=" + permissions.ToString(CultureInfo.InvariantCulture) +
            "&guild_id=" + Uri.EscapeDataString(guildId) +
            "&disable_guild_select=true";
    }

    private async Task<DiscordSession> ExchangeCodeAsync(
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        });
        using HttpResponseMessage response = await Http.PostAsync(
            new Uri(new Uri(DiscordApi), "oauth2/token"), form, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "Discord token değişimi başarısız. Developer Portal'da Public OAuth2 Client seçeneğini ve callback adresini kontrol et.");

        return ParseToken(body, null);
    }

    private async Task<DiscordSession> RefreshSessionAsync(
        DiscordSession current,
        CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken
        });
        using HttpResponseMessage response = await Http.PostAsync(
            new Uri(new Uri(DiscordApi), "oauth2/token"), form, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Discord oturumu yenilenemedi; tekrar bağlan.");
        return ParseToken(body, current);
    }

    private static DiscordSession ParseToken(string body, DiscordSession? old)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        string access = root.GetProperty("access_token").GetString() ?? string.Empty;
        string refresh = root.TryGetProperty("refresh_token", out JsonElement r)
            ? r.GetString() ?? old?.RefreshToken ?? string.Empty
            : old?.RefreshToken ?? string.Empty;
        int expiresIn = root.TryGetProperty("expires_in", out JsonElement e) && e.TryGetInt32(out int seconds)
            ? Math.Clamp(seconds, 60, 30 * 24 * 60 * 60)
            : 604800;
        if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh))
            throw new InvalidDataException("Discord eksik OAuth2 token bilgisi döndürdü.");

        return new DiscordSession
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            UserId = old?.UserId ?? string.Empty,
            Username = old?.Username ?? string.Empty,
            GlobalName = old?.GlobalName ?? string.Empty,
            Avatar = old?.Avatar ?? string.Empty,
            Scope = root.TryGetProperty("scope", out JsonElement scope) ? scope.GetString() ?? string.Empty : old?.Scope ?? string.Empty
        };
    }

    private static async Task<DiscordProfile> GetProfileAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(DiscordApi), "users/@me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Discord kullanıcı profili doğrulanamadı.");
        return await response.Content.ReadFromJsonAsync<DiscordProfile>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Discord profil yanıtı boş.");
    }

    private static async Task WriteCallbackPageAsync(
        NetworkStream stream,
        bool success,
        CancellationToken cancellationToken)
    {
        string text = success
            ? "Discord bağlantısı tamamlandı. Bu pencereyi kapatıp YTMT-STUDIO'ya dönebilirsin."
            : "Discord bağlantısı doğrulanamadı. Bu pencereyi kapatıp uygulamaya dön.";
        string color = success ? "#3fb950" : "#f85149";
        string html = "<!doctype html><html lang='tr'><meta charset='utf-8'><title>YTMT-STUDIO</title>" +
            "<body style='background:#0d1117;color:#e6edf3;font:16px Segoe UI,Arial;padding:48px'>" +
            "<h2 style='color:" + color + "'>YTMT-STUDIO</h2><p>" + text +
            "</p></body></html>";
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        string headers = $"HTTP/1.1 {(success ? "200 OK" : "400 Bad Request")}\\r\\n" +
            "Content-Type: text/html; charset=utf-8\\r\\n" +
            $"Content-Length: {bytes.Length}\\r\\n" +
            "Cache-Control: no-store\\r\\nConnection: close\\r\\n\\r\\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            string key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            string value = pair.Length > 1
                ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                : string.Empty;
            values[key] = value;
        }
        return values;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string NormalizeBaseUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("YTMT BOT API adresi geçerli bir HTTPS URL olmalı.", nameof(value));
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

public sealed class DiscordSession
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string GlobalName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}

public sealed class DiscordProfile
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("global_name")] public string GlobalName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
}

public sealed class DiscordGuildInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public bool Owner { get; set; }
    public string Permissions { get; set; } = "0";
    [JsonPropertyName("approximate_member_count")] public int? MemberCount { get; set; }

    private ulong PermissionBits =>
        ulong.TryParse(Permissions, NumberStyles.None, CultureInfo.InvariantCulture, out ulong bits)
            ? bits : 0;

    public bool IsAdministrator => (PermissionBits & (1UL << 3)) != 0;
    public bool CanManageGuild => (PermissionBits & (1UL << 5)) != 0;
    public bool CanManageRoles => (PermissionBits & (1UL << 28)) != 0;
    public bool CanManageServer => Owner || IsAdministrator || CanManageGuild;
}

public sealed class DiscordGuildBotStatus
{
    public bool Installed { get; set; }
    public bool ServiceAvailable { get; set; }
    public string? Error { get; set; }
    public DiscordBotGuildInfo? Guild { get; set; }
    public DiscordBotIdentity? Bot { get; set; }
}

public sealed class DiscordBotGuildInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MemberCount { get; set; }
}

public sealed class DiscordBotIdentity
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public sealed class DiscordGuildResources
{
    public List<DiscordRoleInfo> Roles { get; set; } = [];
    public List<DiscordChannelInfo> Channels { get; set; } = [];
}

public sealed class DiscordRoleInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public int Color { get; set; }
    public bool Managed { get; set; }
    public bool Editable { get; set; }
}

public sealed class DiscordChannelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; }
}
