namespace YTMT_STUDIO.Models;

/// <summary>
/// One local API health-check result. This is stored only by YTMT-STUDIO.
/// </summary>
public sealed record ApiCheckRecord(
    DateTimeOffset Timestamp,
    bool Success,
    double LatencyMs,
    int? StatusCode,
    string? Error);