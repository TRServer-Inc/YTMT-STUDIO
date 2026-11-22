using System.Globalization;
using System.Text.Json;

namespace YTMT_STUDIO.Models;

public sealed class LiveEvent
{
    public long Id { get; init; }
    public string Type { get; init; } = "http";
    public string Name { get; init; } = "Bilinmeyen istek";
    public string Status { get; init; } = "success";
    public double? DurationMs { get; init; }
    public DateTimeOffset? Timestamp { get; init; }

    public static LiveEvent? FromJson(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        return new LiveEvent
        {
            Id = ReadInt64(root, "id") ?? 0,
            Type = ReadString(root, "type") ?? "http",
            Name = ReadString(root, "name") ?? "Bilinmeyen istek",
            Status = ReadString(root, "status") ?? "success",
            DurationMs = ReadDouble(root, "durationMs"),
            Timestamp = ReadTimestamp(root, "timestamp")
        };
    }

    private static JsonElement? Find(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        JsonElement? value = Find(root, name);

        if (!value.HasValue ||
            value.Value.ValueKind != JsonValueKind.String)
            return null;

        return value.Value.GetString();
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        JsonElement? value = Find(root, name);

        if (!value.HasValue)
            return null;

        if (value.Value.ValueKind == JsonValueKind.Number &&
            value.Value.TryGetInt64(out long number))
        {
            return number;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement root, string name)
    {
        JsonElement? value = Find(root, name);

        if (!value.HasValue)
            return null;

        if (value.Value.ValueKind == JsonValueKind.Number &&
            value.Value.TryGetDouble(out double number) &&
            double.IsFinite(number))
        {
            return number;
        }

        return null;
    }

    private static DateTimeOffset? ReadTimestamp(
        JsonElement root,
        string name)
    {
        string? value = ReadString(root, name);

        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset result)
                ? result
                : null;
    }
}