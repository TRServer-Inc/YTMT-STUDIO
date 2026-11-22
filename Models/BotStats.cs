using System.Globalization;
using System.Text.Json;

namespace YTMT_STUDIO.Models;

/// <summary>
/// YTMT BOT GET /api/stats yanıtını temsil eder.
/// Bulunmayan ölçümler null kalır.
/// </summary>
public sealed class BotStats
{
    public bool? DiscordOnline { get; private init; }

    public double? UptimeSeconds { get; private init; }
    public double? WebSocketPingMs { get; private init; }

    public long? NodeRssBytes { get; private init; }
    public long? HeapUsedBytes { get; private init; }
    public long? HeapTotalBytes { get; private init; }
    public long? ExternalBytes { get; private init; }
    public long? ArrayBuffersBytes { get; private init; }

    public long? ContainerMemoryUsedBytes { get; private init; }
    public long? ContainerMemoryLimitBytes { get; private init; }
    public double? ContainerMemoryPercent { get; private init; }

    public double? ProcessCpuPercent { get; private init; }
    public double? HostCpuPercent { get; private init; }

    public double? LoadAverage1m { get; private init; }
    public int? CpuCoreCount { get; private init; }
    public int? ProcessId { get; private init; }

    public int? CommandCount { get; private init; }

    public string? NodeVersion { get; private init; }
    public string? Platform { get; private init; }
    public string? Architecture { get; private init; }

    public static BotStats FromJson(JsonElement root)
    {
        JsonElement? containerMemory = Find(
            root,
            "containerMemory");

        long? containerUsed =
            ReadInt64(Find(root,
                "containerMemoryUsedBytes",
                "memoryUsedBytes",
                "cgroupMemoryUsedBytes"))
            ??
            (containerMemory.HasValue
                ? ReadInt64(Find(containerMemory.Value,
                    "used",
                    "current"))
                : null);

        long? containerLimit =
            ReadInt64(Find(root,
                "containerMemoryLimitBytes",
                "memoryLimitBytes",
                "cgroupMemoryLimitBytes",
                "totalMemoryBytes"))
            ??
            (containerMemory.HasValue
                ? ReadInt64(Find(containerMemory.Value,
                    "limit",
                    "max"))
                : null);

        double? explicitContainerPercent =
            ReadPercent(Find(root,
                "containerMemoryPercent",
                "containerMemoryUsagePercent",
                "cgroupMemoryPercent"))
            ??
            (containerMemory.HasValue
                ? ReadPercent(Find(containerMemory.Value,
                    "percentage",
                    "percent"))
                : null);

        double? calculatedContainerPercent =
            containerUsed.HasValue && containerLimit is > 0
                ? containerUsed.Value * 100d / containerLimit.Value
                : null;

        return new BotStats
        {
            DiscordOnline = ReadBoolean(Find(root,
                "discordStatus",
                "discordConnected",
                "discordOnline",
                "isReady",
                "ready")),

            UptimeSeconds = ReadNumber(Find(root,
                "uptimeSeconds",
                "processUptimeSeconds",
                "uptime")),

            WebSocketPingMs = ReadNumber(Find(root,
                "webSocketPing",
                "websocketPingMs",
                "websocketPing",
                "wsPing",
                "ping")),

            NodeRssBytes = ReadInt64(Find(root,
                "nodeRss",
                "rssBytes",
                "memoryRssBytes",
                "residentSetSizeBytes",
                "rss")),

            HeapUsedBytes = ReadInt64(Find(root,
                "nodeHeapUsed",
                "heapUsedBytes",
                "heapUsed")),

            HeapTotalBytes = ReadInt64(Find(root,
                "nodeHeapTotal",
                "heapTotalBytes",
                "heapTotal")),

            ExternalBytes = ReadInt64(Find(root,
                "nodeExternal",
                "externalBytes",
                "externalMemoryBytes",
                "external")),

            ArrayBuffersBytes = ReadInt64(Find(root,
                "nodeArrayBuffers",
                "arrayBuffersBytes",
                "arrayBuffers")),

            ContainerMemoryUsedBytes = containerUsed,
            ContainerMemoryLimitBytes = containerLimit,

            ContainerMemoryPercent = ClampPercent(
                explicitContainerPercent ?? calculatedContainerPercent),

            ProcessCpuPercent = ReadPercent(Find(root,
                "processCpu",
                "processCpuPercent",
                "processCpuUsagePercent",
                "nodeCpuPercent")),

            HostCpuPercent = ReadPercent(Find(root,
                "hostCpuPercent",
                "systemCpuPercent",
                "cpuUsagePercent",
                "cpuPercent")),

            LoadAverage1m = ReadNumber(Find(root,
                "loadAverage1m",
                "load1",
                "loadAvg1")),

            CpuCoreCount = ReadInt32(Find(root,
                "cpuCoreCount",
                "cpuCount",
                "availableCpus")),

            ProcessId = ReadInt32(Find(root,
                "processId",
                "pid")),

            CommandCount = ReadInt32(Find(root,
                "commandCount",
                "totalCommandCount",
                "commandsCount")),

            NodeVersion = ReadString(Find(root,
                "nodeVersion")),

            Platform = ReadString(Find(root,
                "platform",
                "osPlatform")),

            Architecture = ReadString(Find(root,
                "architecture",
                "arch"))
        };
    }

    private static JsonElement? Find(
        JsonElement element,
        params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (names.Any(name =>
                    string.Equals(
                        name,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return property.Value;
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                JsonElement? nested = Find(property.Value, names);

                if (nested.HasValue)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                JsonElement? nested = Find(item, names);

                if (nested.HasValue)
                    return nested;
            }
        }

        return null;
    }

    private static double? ReadNumber(JsonElement? value)
    {
        if (!value.HasValue)
            return null;

        JsonElement item = value.Value;

        if (item.ValueKind == JsonValueKind.Number &&
            item.TryGetDouble(out double number) &&
            double.IsFinite(number))
        {
            return number;
        }

        if (item.ValueKind == JsonValueKind.String &&
            double.TryParse(
                item.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number) &&
            double.IsFinite(number))
        {
            return number;
        }

        return null;
    }

    private static long? ReadInt64(JsonElement? value)
    {
        if (!value.HasValue)
            return null;

        JsonElement item = value.Value;

        if (item.ValueKind == JsonValueKind.Number &&
            item.TryGetInt64(out long integer))
        {
            return integer >= 0 ? integer : null;
        }

        double? number = ReadNumber(value);

        if (!number.HasValue ||
            number.Value < 0 ||
            number.Value > long.MaxValue)
        {
            return null;
        }

        return (long)number.Value;
    }

    private static int? ReadInt32(JsonElement? value)
    {
        long? number = ReadInt64(value);

        return number.HasValue && number.Value <= int.MaxValue
            ? (int)number.Value
            : null;
    }

    private static double? ReadPercent(JsonElement? value)
    {
        return ClampPercent(ReadNumber(value));
    }

    private static double? ClampPercent(double? value)
    {
        if (!value.HasValue ||
            value.Value < 0 ||
            value.Value > 100)
        {
            return null;
        }

        return value;
    }

    private static string? ReadString(JsonElement? value)
    {
        if (!value.HasValue ||
            value.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? result = value.Value.GetString();

        return string.IsNullOrWhiteSpace(result)
            ? null
            : result.Trim();
    }

    private static bool? ReadBoolean(JsonElement? value)
    {
        if (!value.HasValue)
            return null;

        JsonElement item = value.Value;

        if (item.ValueKind == JsonValueKind.True)
            return true;

        if (item.ValueKind == JsonValueKind.False)
            return false;

        if (item.ValueKind == JsonValueKind.Number &&
            item.TryGetInt32(out int numeric))
        {
            if (numeric == 1) return true;
            if (numeric == 0) return false;
        }

        if (item.ValueKind == JsonValueKind.String)
        {
            string? text = item.GetString();

            if (bool.TryParse(text, out bool parsed))
                return parsed;

            if (string.Equals(text, "online", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "ready", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "connected", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "offline", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "disconnected", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return null;
    }
}