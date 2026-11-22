namespace YTMT_STUDIO.Models;

public sealed class ApiResult
{
    public bool Success { get; init; }
    public int? StatusCode { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string RawJson { get; init; } = string.Empty;
    public string? Error { get; init; }
    public BotStats? Stats { get; init; }
}