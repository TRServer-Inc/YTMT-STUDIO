using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using YTMT_STUDIO.Models;

namespace YTMT_STUDIO.Services;

/// <summary>
/// Stores a bounded local history of API checks under the current user's AppData.
/// It never writes bot tokens or request bodies.
/// </summary>
public sealed class MonitoringHistoryService
{
    public const int MaximumRecords = 500;
    private const long CompactAtBytes = 384 * 1024;

    private readonly string _historyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YTMT-STUDIO", "monitor-history.jsonl");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public List<ApiCheckRecord> Load()
    {
        try
        {
            if (!File.Exists(_historyPath))
                return [];

            var records = new List<ApiCheckRecord>();
            foreach (string line in File.ReadLines(_historyPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    ApiCheckRecord? item = JsonSerializer.Deserialize<ApiCheckRecord>(line, JsonOptions);
                    if (item is not null && double.IsFinite(item.LatencyMs) && item.LatencyMs >= 0)
                        records.Add(item);
                }
                catch (JsonException)
                {
                    // A damaged line must not make the rest of the history unusable.
                }
            }

            return records.TakeLast(MaximumRecords).ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Append(ApiCheckRecord record)
    {
        string? directory = Path.GetDirectoryName(_historyPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string line = JsonSerializer.Serialize(record, JsonOptions);
        File.AppendAllText(_historyPath, line + Environment.NewLine, new UTF8Encoding(false));

        var info = new FileInfo(_historyPath);
        if (info.Exists && info.Length >= CompactAtBytes)
            Compact();
    }

    public void Clear()
    {
        string? directory = Path.GetDirectoryName(_historyPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_historyPath, string.Empty, new UTF8Encoding(false));
    }

    private void Compact()
    {
        List<ApiCheckRecord> records = Load();
        string temporaryPath = _historyPath + ".tmp";

        try
        {
            string content = string.Join(
                Environment.NewLine,
                records.Select(record => JsonSerializer.Serialize(record, JsonOptions)));

            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, _historyPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}