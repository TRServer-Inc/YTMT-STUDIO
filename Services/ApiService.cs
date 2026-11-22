using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using YTMT_STUDIO.Models;

namespace YTMT_STUDIO.Services;

public sealed class ApiService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly HttpClient _streamHttpClient;

    public ApiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "YTMT-STUDIO/1.3");

        _streamHttpClient = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        _streamHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "YTMT-STUDIO/1.3");

        _streamHttpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    // ==========================================
    // NORMAL STATS API
    // ==========================================

    public async Task<ApiResult> GetStatsAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            string normalized = NormalizeBaseUrl(baseUrl);

            var endpoint = new Uri(
                new Uri(normalized, UriKind.Absolute),
                "api/stats");

            using HttpResponseMessage response = await _httpClient.GetAsync(
                endpoint,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            string body = await response.Content.ReadAsStringAsync(
                cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new ApiResult
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    Elapsed = stopwatch.Elapsed,
                    RawJson = body,
                    Error = $"Sunucu HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
                };
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new ApiResult
                    {
                        Success = false,
                        StatusCode = (int)response.StatusCode,
                        Elapsed = stopwatch.Elapsed,
                        RawJson = body,
                        Error = "API yanıtı JSON nesnesi değil."
                    };
                }

                return new ApiResult
                {
                    Success = true,
                    StatusCode = (int)response.StatusCode,
                    Elapsed = stopwatch.Elapsed,
                    RawJson = body,
                    Stats = BotStats.FromJson(document.RootElement)
                };
            }
            catch (JsonException)
            {
                return new ApiResult
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    Elapsed = stopwatch.Elapsed,
                    RawJson = body,
                    Error = "API yanıtı geçerli JSON biçiminde değil."
                };
            }
        }
        catch (TaskCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            return new ApiResult
            {
                Success = false,
                Elapsed = stopwatch.Elapsed,
                Error = "İstek zaman aşımına uğradı (12 saniye)."
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();

            return new ApiResult
            {
                Success = false,
                Elapsed = stopwatch.Elapsed,
                Error = "HTTP bağlantı hatası: " + ex.Message
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new ApiResult
            {
                Success = false,
                Elapsed = stopwatch.Elapsed,
                Error = ex.Message
            };
        }
    }

    // ==========================================
    // LIVE SERVER-SENT EVENTS
    // ==========================================

    public async Task StreamEventsAsync(
        string baseUrl,
        Action<LiveEvent> onEvent,
        Action? onConnected,
        CancellationToken cancellationToken = default)
    {
        string normalized = NormalizeBaseUrl(baseUrl);

        var endpoint = new Uri(
            new Uri(normalized, UriKind.Absolute),
            "api/events/stream");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            endpoint);

        request.Headers.Accept.Clear();

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using HttpResponseMessage response = await _streamHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Canlı akış HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                null,
                response.StatusCode);
        }

        onConnected?.Invoke();

        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);

        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? eventName = null;
        var data = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    ProcessSseMessage(
                        eventName,
                        data.ToString(),
                        onEvent);
                }

                eventName = null;
                data.Clear();

                continue;
            }

            // SSE yorumları / heartbeat satırları.
            if (line.StartsWith(':'))
                continue;

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                    data.Append('\n');

                data.Append(line[5..].TrimStart());
            }
        }
    }

    private static void ProcessSseMessage(
        string? eventName,
        string json,
        Action<LiveEvent> onEvent)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            JsonElement root = document.RootElement;

            // İlk bağlantıda sunucu son olayların listesini yollar.
            if (eventName == "snapshot" &&
                root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in root.EnumerateArray())
                {
                    LiveEvent? liveEvent = LiveEvent.FromJson(item);

                    if (liveEvent is not null)
                        onEvent(liveEvent);
                }

                return;
            }

            // Yeni bir olay geldi.
            if (root.ValueKind == JsonValueKind.Object)
            {
                LiveEvent? liveEvent = LiveEvent.FromJson(root);

                if (liveEvent is not null)
                    onEvent(liveEvent);
            }
        }
        catch (JsonException)
        {
            // Hatalı tek bir olay tüm akışı durdurmasın.
            Debug.WriteLine("[LIVE STREAM] Geçersiz JSON olayı.");
        }
    }

    // ==========================================
    // URL NORMALIZATION
    // ==========================================

    public static string NormalizeBaseUrl(string value)
    {
        if (!Uri.TryCreate(
                value?.Trim(),
                UriKind.Absolute,
                out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Geçerli bir https:// veya http:// ana adresi gir. Kullanıcı bilgisi, sorgu ve # parçası kullanma.");
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }

    // ==========================================
    // DISPOSE
    // ==========================================

    public void Dispose()
    {
        _httpClient.Dispose();
        _streamHttpClient.Dispose();
    }
}