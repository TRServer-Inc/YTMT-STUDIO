using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using YTMT_STUDIO.Models;
using YTMT_STUDIO.Services;

namespace YTMT_STUDIO;

public sealed partial class MainForm
{
    private const int MaximumStoredLiveEvents = 2_000;
    private const int MaximumVisibleLiveEvents = 100;

    private readonly MonitoringHistoryService _monitorHistoryService = new();
    private List<ApiCheckRecord> _monitorHistory = [];

    private Panel? _monitorPage;
    private DataGridView? _monitorGrid;
    private Label? _monitorSuccessRate;
    private Label? _monitorAverageLatency;
    private Label? _monitorFailureCount;
    private Label? _monitorLastCheck;

    private readonly List<LiveEvent> _liveEventHistory = [];
    private bool _liveEventsPaused;
    private TextBox? _liveEventSearch;
    private ComboBox? _liveEventSourceFilter;
    private ComboBox? _liveEventStatusFilter;
    private Button? _livePauseButton;

    private void InitializeMonitorHistory()
    {
        _monitorHistory = _monitorHistoryService.Load();
    }

    // ==========================================
    // API HEALTH MONITOR
    // ==========================================

    private Panel CreateMonitorPage()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            Padding = new Padding(0, 0, 0, 4)
        };

        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 50,
            ForeColor = Palette.Muted,
            Text = "Son API kontrollerinin yerel geçmişi. Kayıtlar bu Windows hesabında tutulur; bot sunucusuna yazılmaz.",
            TextAlign = ContentAlignment.MiddleLeft
        };

        var summary = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 105,
            BackColor = Palette.Background,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 8)
        };

        summary.Controls.Add(CreateMonitorSummaryCard(
            "SON 100 KONTROLDE BAŞARI", out _monitorSuccessRate));
        summary.Controls.Add(CreateMonitorSummaryCard(
            "ORTALAMA API YANITI", out _monitorAverageLatency));
        summary.Controls.Add(CreateMonitorSummaryCard(
            "HATALI KONTROL", out _monitorFailureCount));
        summary.Controls.Add(CreateMonitorSummaryCard(
            "SON KONTROL", out _monitorLastCheck));

        var tablePanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(10)
        };

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Palette.Surface
        };

        var export = CreateSmallButton("CSV Dışa Aktar", 118);
        export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        export.Click += (_, _) => ExportMonitorHistory();

        var clear = CreateSmallButton("Geçmişi Temizle", 125);
        clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clear.Click += (_, _) => ClearMonitorHistory();

        var refresh = CreateSmallButton("API'yi Kontrol Et", 132);
        refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refresh.Click += (_, _) => _ = RefreshDashboardAsync();

        toolbar.Controls.AddRange([export, clear, refresh]);
        toolbar.Resize += (_, _) =>
        {
            refresh.Location = new Point(Math.Max(0, toolbar.ClientSize.Width - refresh.Width), 2);
            clear.Location = new Point(Math.Max(0, refresh.Left - clear.Width - 8), 2);
            export.Location = new Point(Math.Max(0, clear.Left - export.Width - 8), 2);
        };
        toolbar.PerformLayout();
        refresh.Location = new Point(Math.Max(0, toolbar.ClientSize.Width - refresh.Width), 2);
        clear.Location = new Point(Math.Max(0, refresh.Left - clear.Width - 8), 2);
        export.Location = new Point(Math.Max(0, clear.Left - export.Width - 8), 2);

        _monitorGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Palette.Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Palette.Border,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            ScrollBars = ScrollBars.Both
        };

        StyleDataGrid(_monitorGrid);
        _monitorGrid.Columns.Add("time", "ZAMAN");
        _monitorGrid.Columns.Add("result", "SONUÇ");
        _monitorGrid.Columns.Add("statusCode", "HTTP");
        _monitorGrid.Columns.Add("latency", "YANIT SÜRESİ");
        _monitorGrid.Columns.Add("error", "HATA / AÇIKLAMA");
        _monitorGrid.Columns[0].FillWeight = 26;
        _monitorGrid.Columns[1].FillWeight = 14;
        _monitorGrid.Columns[2].FillWeight = 10;
        _monitorGrid.Columns[3].FillWeight = 16;
        _monitorGrid.Columns[4].FillWeight = 40;
        _monitorGrid.RowTemplate.Height = 30;

        tablePanel.Controls.Add(_monitorGrid);
        tablePanel.Controls.Add(toolbar);

        root.Controls.Add(tablePanel);
        root.Controls.Add(summary);
        root.Controls.Add(heading);

        UpdateMonitorPage();
        return root;
    }

    private Panel CreateMonitorSummaryCard(string title, out Label value)
    {
        var card = new Panel
        {
            Width = 174,
            Height = 82,
            BackColor = Palette.Surface,
            Margin = new Padding(0, 0, 10, 8),
            Padding = new Padding(12)
        };

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            Location = new Point(12, 10)
        };

        value = new Label
        {
            Text = "—",
            AutoSize = false,
            Width = 150,
            Height = 32,
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            Location = new Point(12, 34),
            AutoEllipsis = true
        };

        card.Controls.AddRange([titleLabel, value]);
        return card;
    }

    private static void StyleDataGrid(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceHover;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(31, 48, 66);
        grid.DefaultCellStyle.SelectionForeColor = Palette.Text;
        grid.DefaultCellStyle.Padding = new Padding(6, 3, 6, 3);
    }

    private void RecordApiCheck(ApiResult result)
    {
        var record = new ApiCheckRecord(
            DateTimeOffset.Now,
            result.Success,
            Math.Max(0, result.Elapsed.TotalMilliseconds),
            result.StatusCode,
            LimitText(result.Error, 300));

        _monitorHistory.Add(record);
        while (_monitorHistory.Count > MonitoringHistoryService.MaximumRecords)
            _monitorHistory.RemoveAt(0);

        try
        {
            _monitorHistoryService.Append(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[MONITOR HISTORY] " + ex.Message);
        }

        UpdateMonitorPage();
    }

    private void UpdateMonitorPage()
    {
        if (_monitorSuccessRate is null ||
            _monitorAverageLatency is null ||
            _monitorFailureCount is null ||
            _monitorLastCheck is null)
        {
            return;
        }

        List<ApiCheckRecord> recent = _monitorHistory.TakeLast(100).ToList();
        int successful = recent.Count(record => record.Success);
        int failed = recent.Count - successful;
        double successPercent = recent.Count == 0 ? 0 : successful * 100d / recent.Count;
        var successfulRecords = recent.Where(record => record.Success).ToList();

        _monitorSuccessRate.Text = recent.Count == 0
            ? "Henüz veri yok"
            : $"%{successPercent:0.0}";
        _monitorSuccessRate.ForeColor = recent.Count == 0
            ? Palette.Text
            : successPercent >= 95 ? Palette.Green : successPercent >= 75 ? Palette.Yellow : Palette.Red;

        _monitorAverageLatency.Text = successfulRecords.Count == 0
            ? "—"
            : $"{successfulRecords.Average(record => record.LatencyMs):0} ms";

        _monitorFailureCount.Text = failed.ToString(CultureInfo.InvariantCulture);
        _monitorFailureCount.ForeColor = failed == 0 ? Palette.Green : Palette.Red;

        _monitorLastCheck.Text = _monitorHistory.Count == 0
            ? "—"
            : _monitorHistory[^1].Timestamp.ToLocalTime().ToString("HH:mm:ss  dd.MM.yyyy");

        if (_monitorGrid is null || _monitorGrid.IsDisposed)
            return;

        _monitorGrid.Rows.Clear();
        foreach (ApiCheckRecord record in _monitorHistory.AsEnumerable().Reverse().Take(200))
        {
            int rowIndex = _monitorGrid.Rows.Add(
                record.Timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
                record.Success ? "BAŞARILI" : "HATA",
                record.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "—",
                $"{record.LatencyMs:0.0} ms",
                record.Error ?? string.Empty);

            _monitorGrid.Rows[rowIndex].Cells["result"].Style.ForeColor =
                record.Success ? Palette.Green : Palette.Red;
        }
    }

    private void ExportMonitorHistory()
    {
        if (_monitorHistory.Count == 0)
        {
            MessageBox.Show("Dışa aktarılacak API geçmişi bulunmuyor.",
                "YTMT-STUDIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "API geçmişini dışa aktar",
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"ytmt-api-gecmisi-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = "csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var lines = new List<string>
        {
            "timestamp,success,http_status,latency_ms,error"
        };

        lines.AddRange(_monitorHistory.Select(record => string.Join(',', new[]
        {
            CsvEscape(record.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
            record.Success ? "true" : "false",
            record.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.LatencyMs.ToString("0.0", CultureInfo.InvariantCulture),
            CsvEscape(record.Error ?? string.Empty)
        })));

        try
        {
            File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, lines), new UTF8Encoding(true));
            MessageBox.Show($"{_monitorHistory.Count} kayıt CSV dosyasına aktarıldı.",
                "Dışa aktarma tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("CSV dosyası yazılamadı: " + ex.Message,
                "Dışa aktarma hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearMonitorHistory()
    {
        DialogResult answer = MessageBox.Show(
            "Yerel API kontrol geçmişinin tamamı silinsin mi? Bu işlem bot verilerine dokunmaz.",
            "Geçmişi temizle", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            _monitorHistoryService.Clear();
            _monitorHistory.Clear();
            UpdateMonitorPage();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Geçmiş temizlenemedi: " + ex.Message,
                "Dosya hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ==========================================
    // LIVE EVENT FILTERING AND EXPORT
    // ==========================================

    private Control CreateLiveEventToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            WrapContents = true,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 2),
            BackColor = Palette.Surface
        };

        _liveEventSearch = new TextBox
        {
            Width = 170,
            Height = 30,
            Margin = new Padding(0, 4, 8, 4),
            PlaceholderText = "İstek veya komut ara...",
            BackColor = Palette.SurfaceHover,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle
        };

        _liveEventSourceFilter = new ComboBox
        {
            Width = 105,
            Height = 30,
            Margin = new Padding(0, 4, 8, 4),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.SurfaceHover,
            ForeColor = Palette.Text
        };
        _liveEventSourceFilter.Items.AddRange(["Tüm kaynaklar", "HTTP", "Discord"]);
        _liveEventSourceFilter.SelectedIndex = 0;

        _liveEventStatusFilter = new ComboBox
        {
            Width = 105,
            Height = 30,
            Margin = new Padding(0, 4, 8, 4),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Palette.SurfaceHover,
            ForeColor = Palette.Text
        };
        _liveEventStatusFilter.Items.AddRange(["Tüm sonuçlar", "Başarılı", "Hata"]);
        _liveEventStatusFilter.SelectedIndex = 0;

        _livePauseButton = CreateSmallButton("Duraklat", 82);
        _livePauseButton.Height = 30;
        _livePauseButton.Margin = new Padding(0, 4, 8, 4);
        _livePauseButton.Click += (_, _) =>
        {
            _liveEventsPaused = !_liveEventsPaused;
            _livePauseButton.Text = _liveEventsPaused ? "Devam Et" : "Duraklat";
            if (!_liveEventsPaused)
                RenderLiveEvents();
            else if (_liveEventCount is not null)
                _liveEventCount.Text = $"Akış duraklatıldı • Kayıtlı: {_liveEventHistory.Count} • Alınan toplam: {_liveEventCountValue}";
        };

        var clear = CreateSmallButton("Temizle", 76);
        clear.Height = 30;
        clear.Margin = new Padding(0, 4, 8, 4);
        clear.Click += (_, _) => ClearLiveEvents();

        var export = CreateSmallButton("CSV", 62);
        export.Height = 30;
        export.Margin = new Padding(0, 4, 8, 4);
        export.Click += (_, _) => ExportLiveEvents();

        _liveEventSearch.TextChanged += (_, _) =>
        {
            if (!_liveEventsPaused)
                RenderLiveEvents();
        };
        _liveEventSourceFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_liveEventsPaused)
                RenderLiveEvents();
        };
        _liveEventStatusFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_liveEventsPaused)
                RenderLiveEvents();
        };

        bar.Controls.AddRange([
            _liveEventSearch,
            _liveEventSourceFilter,
            _liveEventStatusFilter,
            _livePauseButton,
            clear,
            export
        ]);

        return bar;
    }

    private void RenderLiveEvents()
    {
        if (_liveEventsPaused || _liveEventsGrid is null || _liveEventsGrid.IsDisposed)
            return;

        string query = _liveEventSearch?.Text.Trim() ?? string.Empty;
        string sourceFilter = _liveEventSourceFilter?.SelectedItem?.ToString() ?? "Tüm kaynaklar";
        string statusFilter = _liveEventStatusFilter?.SelectedItem?.ToString() ?? "Tüm sonuçlar";

        IEnumerable<LiveEvent> filtered = _liveEventHistory;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Status.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (sourceFilter == "HTTP")
            filtered = filtered.Where(item => !item.Type.Equals("discord", StringComparison.OrdinalIgnoreCase));
        else if (sourceFilter == "Discord")
            filtered = filtered.Where(item => item.Type.Equals("discord", StringComparison.OrdinalIgnoreCase));

        if (statusFilter == "Başarılı")
            filtered = filtered.Where(IsSuccessfulLiveEvent);
        else if (statusFilter == "Hata")
            filtered = filtered.Where(item => !IsSuccessfulLiveEvent(item));

        List<LiveEvent> visible = filtered.Take(MaximumVisibleLiveEvents).ToList();
        _liveEventsGrid.Rows.Clear();

        foreach (LiveEvent item in visible)
        {
            string source = item.Type.Equals("discord", StringComparison.OrdinalIgnoreCase)
                ? "DISCORD"
                : "HTTP";
            string time = item.Timestamp?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
            string duration = item.DurationMs.HasValue ? $"{item.DurationMs.Value:0} ms" : "—";
            bool success = IsSuccessfulLiveEvent(item);

            int rowIndex = _liveEventsGrid.Rows.Add(
                time, source, item.Name, duration, success ? "BAŞARILI" : "HATA");
            _liveEventsGrid.Rows[rowIndex].Cells["source"].Style.ForeColor =
                source == "DISCORD" ? Palette.Blue : Palette.Muted;
            _liveEventsGrid.Rows[rowIndex].Cells["status"].Style.ForeColor =
                success ? Palette.Green : Palette.Red;
        }

        if (_liveEventCount is not null)
        {
            _liveEventCount.Text =
                $"Gösterilen: {visible.Count} / {_liveEventHistory.Count}  •  Alınan toplam: {_liveEventCountValue}";
        }

        if (_liveEventsGrid.Rows.Count > 0)
        {
            _liveEventsGrid.ClearSelection();
            _liveEventsGrid.Rows[0].Selected = true;
            _liveEventsGrid.FirstDisplayedScrollingRowIndex = 0;
        }
    }

    private static bool IsSuccessfulLiveEvent(LiveEvent item) =>
        item.Status.Equals("success", StringComparison.OrdinalIgnoreCase) ||
        item.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
        item.Status.Equals("200", StringComparison.OrdinalIgnoreCase);

    private void ClearLiveEvents()
    {
        DialogResult answer = MessageBox.Show(
            "Ekranda tutulan canlı olay geçmişi temizlensin mi? Bu işlem bot tarafındaki kayıtları silmez.",
            "Canlı olayları temizle", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        _liveEventHistory.Clear();
        _liveEventCountValue = 0;
        _liveEventsGrid?.Rows.Clear();

        if (_liveEventCount is not null)
            _liveEventCount.Text = "Gösterilen: 0 • Alınan toplam: 0";
    }

    private void ExportLiveEvents()
    {
        if (_liveEventHistory.Count == 0)
        {
            MessageBox.Show("Dışa aktarılacak canlı olay bulunmuyor.",
                "YTMT-STUDIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Canlı olayları dışa aktar",
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"ytmt-canli-olaylar-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = "csv"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var lines = new List<string> { "timestamp,type,name,status,duration_ms,id" };
        lines.AddRange(_liveEventHistory.AsEnumerable().Reverse().Select(item => string.Join(',', new[]
        {
            CsvEscape(item.Timestamp?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
            CsvEscape(item.Type),
            CsvEscape(item.Name),
            CsvEscape(item.Status),
            item.DurationMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? string.Empty,
            item.Id.ToString(CultureInfo.InvariantCulture)
        })));

        try
        {
            File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, lines), new UTF8Encoding(true));
            MessageBox.Show($"{_liveEventHistory.Count} olay CSV dosyasına aktarıldı.",
                "Dışa aktarma tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("CSV dosyası yazılamadı: " + ex.Message,
                "Dışa aktarma hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==========================================
    // SHARED UTILITIES
    // ==========================================

    private void CopyRawJson()
    {
        if (string.IsNullOrWhiteSpace(_rawJson.Text))
        {
            MessageBox.Show("Kopyalanacak JSON verisi yok.",
                "YTMT-STUDIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Clipboard.SetText(_rawJson.Text);
            _connectionBadge.Text = "●  JSON PANoya KOPYALANDI";
            _connectionBadge.ForeColor = Palette.Blue;
        }
        catch (Exception ex)
        {
            MessageBox.Show("JSON panoya kopyalanamadı: " + ex.Message,
                "Pano hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveRawJson()
    {
        if (string.IsNullOrWhiteSpace(_rawJson.Text))
        {
            MessageBox.Show("Kaydedilecek JSON verisi yok.",
                "YTMT-STUDIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "API yanıtını kaydet",
            Filter = "JSON dosyası (*.json)|*.json|Metin dosyası (*.txt)|*.txt",
            FileName = $"ytmt-api-stats-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = "json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, _rawJson.Text, new UTF8Encoding(true));
            _connectionBadge.Text = "●  JSON DOSYASI KAYDEDİLDİ";
            _connectionBadge.ForeColor = Palette.Green;
        }
        catch (Exception ex)
        {
            MessageBox.Show("JSON dosyası kaydedilemedi: " + ex.Message,
                "Dosya hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenExternalUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            MessageBox.Show("Açılacak adres geçerli bir HTTP/HTTPS bağlantısı değil.",
                "Geçersiz adres", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Tarayıcı açılamadı: " + ex.Message,
                "Bağlantı hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    private static string? LimitText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}