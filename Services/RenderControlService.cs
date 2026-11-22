using System.Net.Http.Headers;
using System.Text.Json;

namespace YTMT_STUDIO.Services;

public sealed record RenderServiceInfo(
    string Id,
    string Name,
    string Type,
    string OwnerId,
    bool IsSuspended,
    string? ServiceUrl,
    string? DashboardUrl,
    DateTimeOffset? UpdatedAt);

public sealed record RenderLogEntry(
    DateTimeOffset? Timestamp,
    string Level,
    string Message);

/// <summary>
/// Small client for the official Render REST API. The API key is supplied at
/// runtime and is never written to settings or logs by this class.
/// </summary>
public sealed class RenderControlService : IDisposable
{
    private static readonly Uri ApiBaseUri = new("https://api.render.com/v1/");
    private readonly HttpClient _httpClient;

    public RenderControlService(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Render API anahtarı boş olamaz.", nameof(apiKey));

        _httpClient = new HttpClient
        {
            BaseAddress = ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(20)
        };

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("YTMT-STUDIO/1.5");
    }

    public async Task<RenderServiceInfo> GetServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        ValidateServiceId(serviceId);

        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"services/{Uri.EscapeDataString(serviceId.Trim())}",
            cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        string? suspendedValue = ReadString(root, "suspended");
        bool isSuspended = string.Equals(
            suspendedValue, "suspended", StringComparison.OrdinalIgnoreCase);

        string? serviceUrl = null;
        if (root.TryGetProperty("serviceDetails", out JsonElement details) &&
            details.ValueKind == JsonValueKind.Object)
        {
            serviceUrl = ReadString(details, "url");
        }

        DateTimeOffset? updatedAt = null;
        string? updatedAtText = ReadString(root, "updatedAt");
        if (DateTimeOffset.TryParse(updatedAtText, out DateTimeOffset parsedUpdatedAt))
            updatedAt = parsedUpdatedAt;

        return new RenderServiceInfo(
            ReadString(root, "id") ?? serviceId.Trim(),
            ReadString(root, "name") ?? "YTMT BOT",
            ReadString(root, "type") ?? "Render service",
            ReadString(root, "ownerId") ?? string.Empty,
            isSuspended,
            serviceUrl,
            ReadString(root, "dashboardUrl"),
            updatedAt);
    }

    public async Task<IReadOnlyList<RenderLogEntry>> GetRecentApplicationLogsAsync(
        string ownerId,
        string serviceId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Render workspace/owner ID alınamadı.", nameof(ownerId));

        ValidateServiceId(serviceId);
        int safeLimit = Math.Clamp(limit, 1, 100);
        string query =
            $"logs?ownerId={Uri.EscapeDataString(ownerId.Trim())}" +
            $"&resource={Uri.EscapeDataString(serviceId.Trim())}" +
            $"&direction=backward&limit={safeLimit}&type=app";

        using HttpResponseMessage response = await _httpClient.GetAsync(query, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);

        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("logs", out JsonElement logs) ||
            logs.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RenderLogEntry>();
        }

        var result = new List<RenderLogEntry>();
        foreach (JsonElement entry in logs.EnumerateArray())
        {
            string message = ReadString(entry, "message") ?? string.Empty;
            string? timestampText = ReadString(entry, "timestamp");
            DateTimeOffset? timestamp =
                DateTimeOffset.TryParse(timestampText, out DateTimeOffset parsed) ? parsed : null;

            string level = "LOG";
            if (entry.TryGetProperty("labels", out JsonElement labels) &&
                labels.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement label in labels.EnumerateArray())
                {
                    if (string.Equals(ReadString(label, "name"), "level", StringComparison.OrdinalIgnoreCase))
                    {
                        level = ReadString(label, "value") ?? "LOG";
                        break;
                    }
                }
            }

            result.Add(new RenderLogEntry(timestamp, level, message));
        }

        // The API returns newest-first for direction=backward; display chronologically.
        result.Reverse();
        return result;
    }

    public Task ResumeServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default) =>
        SendActionAsync(serviceId, "resume", cancellationToken);

    public Task SuspendServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default) =>
        SendActionAsync(serviceId, "suspend", cancellationToken);

    public Task RestartServiceAsync(
        string serviceId,
        CancellationToken cancellationToken = default) =>
        SendActionAsync(serviceId, "restart", cancellationToken);

    public static bool IsValidServiceId(string? serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return false;

        string value = serviceId.Trim();
        return value.Length is >= 10 and <= 128 &&
               value.StartsWith("srv-", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains('/') &&
               !value.Contains('\\') &&
               !value.Any(char.IsWhiteSpace) &&
               value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private async Task SendActionAsync(
        string serviceId,
        string action,
        CancellationToken cancellationToken)
    {
        ValidateServiceId(serviceId);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"services/{Uri.EscapeDataString(serviceId.Trim())}/{action}");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static void ValidateServiceId(string serviceId)
    {
        if (!IsValidServiceId(serviceId))
            throw new ArgumentException("Render Service ID biçimi geçersiz. srv- ile başlayan hizmet kimliğini gir.", nameof(serviceId));
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
            return;

        string explanation = (int)response.StatusCode switch
        {
            401 => "Render API anahtarı geçersiz veya süresi dolmuş.",
            403 => "API anahtarının bu hizmeti yönetme izni yok.",
            404 => "Render Service ID bulunamadı.",
            402 => "Render bu işlem için hesap/plan koşulu istiyor.",
            409 => "Hizmetin mevcut durumu bu işleme uygun değil.",
            429 => "Render API hız sınırına ulaşıldı. Biraz sonra tekrar dene.",
            _ => $"Render API isteği başarısız oldu (HTTP {(int)response.StatusCode} {response.ReasonPhrase})."
        };

        // Avoid displaying response bodies because they are not needed for the
        // user-facing error and may contain provider diagnostics.
        _ = body;
        throw new HttpRequestException(explanation, null, response.StatusCode);
    }

    public void Dispose() => _httpClient.Dispose();
}