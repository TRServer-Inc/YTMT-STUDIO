using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using YTMT_STUDIO.AuthClient.Models;

namespace YTMT_STUDIO.AuthClient.Services;

public sealed class AuthClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public AuthClient()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = LoadBaseAddress(),
            Timeout = TimeSpan.FromSeconds(15)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YTMT-STUDIO/1.4.1");
    }

    private static Uri LoadBaseAddress()
    {
        string? configured = Environment.GetEnvironmentVariable("YTMT_AUTH_API_URL");
        string configPath = Path.Combine(AppContext.BaseDirectory, "auth.config.json");

        if (string.IsNullOrWhiteSpace(configured) && File.Exists(configPath))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
                if (document.RootElement.TryGetProperty("baseUrl", out JsonElement value))
                    configured = value.GetString();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("auth.config.json geçerli JSON değil.", ex);
            }
        }

        if (string.IsNullOrWhiteSpace(configured) ||
            configured.Contains("YOUR-AUTH-SERVICE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Auth API adresi ayarlanmamış. auth.config.json içindeki baseUrl alanına /api/ içeren Render URL'sini yazın.");
        }

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("Auth API adresi geçerli bir HTTPS temel URL'si olmalıdır.");
        }

        string baseUrl = uri.ToString().TrimEnd('/') + "/";
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "auth.config.json baseUrl değeri /api/ ile bitmelidir. Örnek: https://...onrender.com/api/");
        }

        return new Uri(baseUrl, UriKind.Absolute);
    }

    public async Task<AuthSession> LoginAsync(
        string username,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken = default)
    {
        AuthSession session = await PostForSessionAsync(
            "auth/login",
            new LoginRequest(username, password, rememberMe),
            rememberMe,
            cancellationToken);

        return session;
    }

    // Kept for Program.cs compatibility. A session loaded from the persistent
    // store is a remembered session.
    public Task<AuthSession> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        return RefreshAsync(refreshToken, rememberMe: true, cancellationToken: cancellationToken);
    }

    public Task<AuthSession> RefreshAsync(
        string refreshToken,
        bool rememberMe,
        CancellationToken cancellationToken = default)
    {
        return PostForSessionAsync(
            "auth/refresh",
            new RefreshRequest(refreshToken),
            rememberMe,
            cancellationToken);
    }

    public async Task<AuthSession> ValidateOrRefreshAsync(
        AuthSession currentSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSession);

        if (string.IsNullOrWhiteSpace(currentSession.RefreshToken) ||
            currentSession.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new AuthApiException("Oturumun süresi dolmuş. Yeniden giriş yap.", 401);
        }

        // Refresh shortly before access-token expiry instead of waiting for a 401.
        if (currentSession.AccessExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(45))
        {
            return await RefreshAsync(
                currentSession.RefreshToken,
                currentSession.RememberMe,
                cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", currentSession.AccessToken);

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseContentRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return await RefreshAsync(
                currentSession.RefreshToken,
                currentSession.RememberMe,
                cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, cancellationToken);

        AuthMeResponse? payload = await response.Content.ReadFromJsonAsync<AuthMeResponse>(
            JsonOptions, cancellationToken);

        if (payload?.User is null || string.IsNullOrWhiteSpace(payload.User.Username))
            throw new InvalidDataException("Auth API geçersiz kullanıcı yanıtı döndürdü.");

        currentSession.User = MapUser(payload.User);
        return currentSession;
    }

    public async Task LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "auth/logout", new RefreshRequest(refreshToken), JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw await CreateApiExceptionAsync(response, cancellationToken);
    }

    private async Task<AuthSession> PostForSessionAsync<TRequest>(
        string endpoint,
        TRequest body,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            endpoint, body, JsonOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, cancellationToken);

        AuthSessionResponse? payload = await response.Content.ReadFromJsonAsync<AuthSessionResponse>(
            JsonOptions, cancellationToken);

        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.AccessToken) ||
            string.IsNullOrWhiteSpace(payload.RefreshToken) ||
            payload.User is null ||
            string.IsNullOrWhiteSpace(payload.User.Username) ||
            payload.ExpiresAt is null)
        {
            throw new InvalidDataException(
                "Auth API eksik oturum bilgisi döndürdü. Sunucu güncel auth.js sürümünü kullanıyor mu kontrol edin.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int expiresIn = payload.ExpiresIn is > 0 and <= 3600 ? payload.ExpiresIn.Value : 900;
        DateTimeOffset accessExpiresAt = payload.AccessExpiresAt ?? now.AddSeconds(expiresIn);
        DateTimeOffset refreshExpiresAt = payload.ExpiresAt.Value;

        if (accessExpiresAt <= now || refreshExpiresAt <= now)
            throw new InvalidDataException("Auth API süresi geçmiş oturum bilgisi döndürdü.");

        return new AuthSession
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken,
            AccessExpiresAt = accessExpiresAt,
            ExpiresAt = refreshExpiresAt,
            User = MapUser(payload.User),
            RememberMe = rememberMe
        };
    }

    private static AuthUser MapUser(AuthUserPayload source)
    {
        string username = source.Username ?? string.Empty;
        return new AuthUser
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? username : source.Id,
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(source.DisplayName)
                ? username
                : source.DisplayName,
            Role = AuthRoles.Normalize(source.Role)
        };
    }

    private static async Task<AuthApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Kullanıcı adı veya şifre hatalı ya da oturumun süresi dolmuş.",
            HttpStatusCode.Forbidden => "Bu işlem için yetkiniz bulunmuyor.",
            HttpStatusCode.TooManyRequests => "Çok fazla deneme yapıldı. Bir süre sonra tekrar deneyin.",
            HttpStatusCode.ServiceUnavailable => "Kimlik doğrulama sunucusu şu anda kullanılamıyor.",
            _ => $"Kimlik doğrulama isteği başarısız oldu (HTTP {(int)response.StatusCode})."
        };

        try
        {
            ApiError? error = await response.Content.ReadFromJsonAsync<ApiError>(
                JsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Error))
                message = error.Error;
        }
        catch (JsonException)
        {
            // Keep the safe status-based message if the body is not JSON.
        }

        return new AuthApiException(message, (int)response.StatusCode);
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record LoginRequest(string Username, string Password, bool RememberMe);
    private sealed record RefreshRequest(string RefreshToken);

    private sealed class AuthSessionResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public int? ExpiresIn { get; set; }
        public DateTimeOffset? AccessExpiresAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public AuthUserPayload? User { get; set; }
    }

    private sealed class AuthMeResponse
    {
        public AuthUserPayload? User { get; set; }
    }

    private sealed class AuthUserPayload
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? Role { get; set; }
    }

    private sealed class ApiError
    {
        public string? Error { get; set; }
    }
}