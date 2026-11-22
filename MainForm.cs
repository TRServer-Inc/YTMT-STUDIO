using System.Drawing;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using YTMT_STUDIO.AuthClient.Models;
using YTMT_STUDIO.Controls;
using YTMT_STUDIO.Models;
using YTMT_STUDIO.Services;

namespace YTMT_STUDIO;

public sealed partial class MainForm : Form
{
    private static class Palette
    {
        public static readonly Color Background = Color.FromArgb(13, 17, 23);
        public static readonly Color Sidebar = Color.FromArgb(18, 22, 29);
        public static readonly Color Surface = Color.FromArgb(22, 27, 34);
        public static readonly Color SurfaceHover = Color.FromArgb(33, 38, 45);
        public static readonly Color Border = Color.FromArgb(48, 54, 61);
        public static readonly Color Text = Color.FromArgb(230, 237, 243);
        public static readonly Color Muted = Color.FromArgb(139, 148, 158);
        public static readonly Color Blue = Color.FromArgb(88, 166, 255);
        public static readonly Color Green = Color.FromArgb(63, 185, 80);
        public static readonly Color Red = Color.FromArgb(248, 81, 73);
        public static readonly Color Yellow = Color.FromArgb(210, 153, 34);
    }

    private readonly SettingsService _settingsService = new();
    private AuthSession _authSession;
    private readonly ApiService _apiService = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();

    private AppSettings _settings;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private readonly Panel _contentHost = new();
    private readonly Label _pageTitle = new();
    private readonly Label _connectionBadge = new();
    private Label? _accountRoleCaption;

    private readonly Dictionary<string, Button> _navButtons = new();
    private string _currentPageKey = "dashboard";

    private Panel? _dashboardPage;
    private Panel? _terminalPage;
    private Panel? _commandsPage;
    private Panel? _settingsPage;

    private WebView2? _terminalWebView;
    private bool _terminalInitialized;
    private Label? _terminalState;

    // ==========================================
    // DASHBOARD METRICS
    // ==========================================

    private Label _discordValue = new();
    private Label _discordDetail = new();

    private Label _memoryValue = new();
    private Label _memoryDetail = new();

    private Label _heapValue = new();
    private Label _heapDetail = new();

    private Label _cpuValue = new();
    private Label _cpuDetail = new();

    private Label _containerMemoryDetail = new();
    private Label _runtimeDetail = new();

    private StatsHistoryChart? _historyChart;

    private Label _uptimeValue = new();
    private Label _uptimeDetail = new();

    private Label _pingValue = new();
    private Label _pingDetail = new();

    private Label _apiLatency = new();
    private Label _lastUpdated = new();
    private Label _apiInfo = new();

    private TextBox _rawJson = new();

    // ==========================================
    // LIVE EVENT STREAM
    // ==========================================

    private DataGridView? _liveEventsGrid;
    private Label? _liveStreamStatus;
    private Label? _liveEventCount;

    private CancellationTokenSource? _liveStreamCts;

    private readonly HashSet<string> _liveEventKeys =
        new(StringComparer.Ordinal);

    private readonly Queue<string> _liveEventKeyOrder = new();

    private int _liveEventCountValue;

    private const int MaxRememberedEventKeys = 2_500;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    public MainForm(DiscordSession discordSession, DiscordOAuthService discordOAuth)
    {
        ArgumentNullException.ThrowIfNull(discordSession);
        _discordSession = discordSession;
        _discordOAuth = discordOAuth ?? throw new ArgumentNullException(nameof(discordOAuth));
        string displayName = string.IsNullOrWhiteSpace(discordSession.GlobalName)
            ? discordSession.Username
            : discordSession.GlobalName;
        _authSession = new AuthSession
        {
            AccessExpiresAt = discordSession.ExpiresAtUtc,
            ExpiresAt = discordSession.ExpiresAtUtc,
            User = new AuthUser
            {
                Id = discordSession.UserId,
                Username = discordSession.Username,
                DisplayName = displayName,
                Role = AuthRoles.User
            }
        };

        _settings = _settingsService.Load();
        InitializeMonitorHistory();

        Text = "YTMT-STUDIO";
        string iconPath = Path.Combine(
    AppContext.BaseDirectory,
    "Assets",
    "ytmt.ico"
);

        if (File.Exists(iconPath))
        {
            Icon = new Icon(iconPath);
        }

        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 680);
        Size = new Size(1440, 900);

        BackColor = Palette.Background;
        ForeColor = Palette.Text;

        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildShell();

        _refreshTimer.Interval =
            Math.Clamp(_settings.RefreshIntervalSeconds, 5, 300) * 1000;

        _refreshTimer.Tick += async (_, _) =>
            await RefreshDashboardAsync();

        Shown += async (_, _) =>
        {
            ShowPage("dashboard");

            await RefreshDashboardAsync();

            if (!IsDisposed && !Disposing)
            {
                _refreshTimer.Start();
                StartLiveStream();
            }
        };

        FormClosing += (_, _) =>
        {
            _refreshTimer.Stop();

            // SSE bağlantısını kapat.
            _liveStreamCts?.Cancel();

            _apiService.Dispose();
            _refreshTimer.Dispose();

            _terminalWebView?.Dispose();
        };
    }

    private void RefreshRoleBasedUi()
    {
        if (_accountRoleCaption is not null)
            _accountRoleCaption.Text = $"OTURUM AÇIK · {AuthRoles.GetDisplayName(_authSession.User.Role).ToUpperInvariant()}";

        foreach ((string key, Button button) in _navButtons)
            button.Visible = AuthRoles.CanAccessPage(_authSession.User.Role, key);

        if (!AuthRoles.CanAccessPage(_authSession.User.Role, _currentPageKey))
            ShowPage("dashboard");
    }

    private Task LogoutAsync()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Discord oturumunu bu bilgisayardan kapatmak istiyor musun?",
            "Discord'dan Çıkış Yap",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return Task.CompletedTask;

        _discordOAuth?.ClearSavedSession();
        Close();
        return Task.CompletedTask;
    }

    // ==========================================
    // MAIN WINDOW
    // ==========================================

    private void BuildShell()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 226,
            BackColor = Palette.Sidebar,
            Padding = new Padding(14, 18, 14, 14)
        };

        var brand = new Panel
        {
            Dock = DockStyle.Top,
            Height = 76
        };

        var brandMark = new Label
        {
            Text = "YT",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Palette.Blue,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(2, 5),
            Size = new Size(44, 44)
        };

        var brandName = new Label
        {
            Text = "YTMT-STUDIO",
            Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold),
            ForeColor = Palette.Text,
            Location = new Point(56, 8),
            AutoSize = true
        };

        var brandSub = new Label
        {
            Text = "BOT CONTROL CENTER",
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = Palette.Muted,
            Location = new Point(57, 31),
            AutoSize = true
        };

        brand.Controls.AddRange([brandMark, brandName, brandSub]);
        sidebar.Controls.Add(brand);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Palette.Sidebar,
            Padding = new Padding(0, 12, 0, 0)
        };

        sidebar.Controls.Add(nav);
        sidebar.Controls.SetChildIndex(nav, 0);

        AddNav(nav, "dashboard", "▦   Genel Bakış");
        AddNav(nav, "botcontrol", "⏻   Bot Yönetimi");
        AddNav(nav, "server", "▣   Sunucu Yönetimi");
        AddNav(nav, "terminal", "⌘   Web Terminal");
        AddNav(nav, "commands", "☷   Komutlar");
        AddNav(nav, "monitor", "◷   API İzleme");
        AddNav(nav, "settings", "⚙   Ayarlar");

        var accountFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 92,
            BackColor = Palette.Sidebar,
            Padding = new Padding(2, 8, 2, 2)
        };

        var accountCaption = _accountRoleCaption = new Label
        {
            Text = $"OTURUM AÇIK · {AuthRoles.GetDisplayName(_authSession.User.Role).ToUpperInvariant()}",
            Dock = DockStyle.Top,
            Height = 18,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold)
        };

        var accountName = new Label
        {
            Text = string.IsNullOrWhiteSpace(_authSession.User.DisplayName)
                ? _authSession.User.Username
                : _authSession.User.DisplayName,
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            AutoEllipsis = true
        };

        var logoutButton = new Button
        {
            Text = "↪  Çıkış Yap",
            Dock = DockStyle.Top,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderColor = Palette.Border, BorderSize = 1 },
            BackColor = Palette.Surface,
            ForeColor = Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Font = new Font("Segoe UI", 8.5F),
            Cursor = Cursors.Hand
        };
        logoutButton.Click += async (_, _) => await LogoutAsync();

        accountFooter.Controls.Add(logoutButton);
        accountFooter.Controls.Add(accountName);
        accountFooter.Controls.Add(accountCaption);
        sidebar.Controls.Add(accountFooter);

        var main = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background
        };

        Controls.Add(main);
        Controls.Add(sidebar);

        var topbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = Palette.Background,
            Padding = new Padding(26, 0, 26, 0)
        };

        _pageTitle.Text = "Genel Bakış";
        _pageTitle.Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold);
        _pageTitle.ForeColor = Palette.Text;
        _pageTitle.AutoSize = true;
        _pageTitle.Location = new Point(26, 22);

        topbar.Controls.Add(_pageTitle);

        _connectionBadge.Text = "●  BAĞLANTI BEKLENİYOR";
        _connectionBadge.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        _connectionBadge.ForeColor = Palette.Yellow;
        _connectionBadge.AutoSize = true;
        _connectionBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _connectionBadge.Location = new Point(
            Math.Max(250, topbar.Width - 245),
            27);

        topbar.Resize += (_, _) =>
        {
            _connectionBadge.Location = new Point(
                Math.Max(250, topbar.ClientSize.Width - 245),
                27);
        };

        topbar.Controls.Add(_connectionBadge);

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = Palette.Background;
        _contentHost.Padding = new Padding(24, 8, 24, 22);

        main.Controls.Add(_contentHost);
        main.Controls.Add(topbar);
    }

    private void AddNav(
        FlowLayoutPanel host,
        string key,
        string title)
    {
        var button = new Button
        {
            Text = title,
            Width = 194,
            Height = 44,
            Margin = new Padding(0, 3, 0, 5),
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular),
            ForeColor = Palette.Muted,
            BackColor = Palette.Sidebar,
            Cursor = Cursors.Hand,
            Tag = key,
            Visible = AuthRoles.CanAccessPage(_authSession.User.Role, key)
        };

        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Palette.SurfaceHover;

        button.Click += (_, _) => ShowPage(key);

        _navButtons[key] = button;

        host.Controls.Add(button);
    }

    // ==========================================
    // PAGE NAVIGATION
    // ==========================================

    private void ShowPage(string key)
    {
        if (!AuthRoles.CanAccessPage(_authSession.User.Role, key))
        {
            MessageBox.Show(this,
                "Bu sayfaya erişmek için hesabının rolü yeterli değil.",
                "Yetki gerekli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _currentPageKey = key;

        foreach ((string navKey, Button button) in _navButtons)
        {
            bool active = navKey == key;

            button.BackColor = active
                ? Color.FromArgb(31, 48, 66)
                : Palette.Sidebar;

            button.ForeColor = active
                ? Palette.Blue
                : Palette.Muted;

            button.Font = new Font(
                "Segoe UI",
                9.5F,
                active ? FontStyle.Bold : FontStyle.Regular);
        }

        Control page;

        switch (key)
        {
            case "botcontrol":
                if (!IsRenderControlOwner)
                {
                    MessageBox.Show(this, "Render bot yönetimi yalnızca owner (Sahip) rolüne açıktır.",
                        "Yetki gerekli", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _pageTitle.Text = "Bot Yönetimi";
                _botControlPage ??= CreateBotControlPage();
                page = _botControlPage;
                break;

            case "server":
                _pageTitle.Text = "Sunucu Yönetim Merkezi";
                _serverManagementPage ??= CreateServerManagementPage();
                page = _serverManagementPage;
                break;

            case "terminal":
                _pageTitle.Text = "Web Terminal";
                _terminalPage ??= CreateTerminalPage();
                page = _terminalPage;
                break;

            case "commands":
                _pageTitle.Text = "Komut Kataloğu";
                _commandsPage ??= CreateCommandsPage();
                page = _commandsPage;
                break;

            case "monitor":
                _pageTitle.Text = "API İzleme Merkezi";
                _monitorPage ??= CreateMonitorPage();
                page = _monitorPage;
                UpdateMonitorPage();
                break;

            case "settings":
                _pageTitle.Text = "Ayarlar";
                _settingsPage = CreateSettingsPage();
                page = _settingsPage;
                break;

            default:
                _pageTitle.Text = "Genel Bakış";
                _dashboardPage ??= CreateDashboardPage();
                page = _dashboardPage;
                break;
        }

        _contentHost.Controls.Clear();

        page.Dock = DockStyle.Fill;

        _contentHost.Controls.Add(page);

        if (key == "terminal")
            _ = LoadTerminalAsync();

        if (key == "botcontrol")
            _ = RefreshRenderServiceAsync();

        if (key == "dashboard")
            _ = RefreshDashboardAsync();
    }

    // ==========================================
    // DASHBOARD
    // ==========================================

    private Panel CreateDashboardPage()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Palette.Background
        };

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Palette.Background,
            Padding = new Padding(0)
        };

        content.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 292));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));

        // ======================================
        // INTRO
        // ======================================

        var intro = new Panel
        {
            Dock = DockStyle.Fill
        };

        intro.Controls.Add(new Label
        {
            Text = "YTMT BOT sunucu durumu, bellek kullanımı ve performans ölçümleri",
            AutoSize = true,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 9F),
            Location = new Point(2, 8)
        });

        var refreshNow = CreateSmallButton("ŞİMDİ YENİLE", 122);

        refreshNow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        refreshNow.Location = new Point(650, 3);

        refreshNow.Click += async (_, _) =>
            await RefreshDashboardAsync();

        intro.Controls.Add(refreshNow);

        intro.Resize += (_, _) =>
        {
            refreshNow.Location = new Point(
                Math.Max(4, intro.ClientSize.Width - refreshNow.Width - 4),
                3);
        };

        content.Controls.Add(intro, 0, 0);

        // ======================================
        // METRIC CARDS
        // ======================================

        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            AutoScroll = true,
            BackColor = Palette.Background,
            Padding = new Padding(0, 3, 0, 4)
        };

        cards.Controls.Add(
            CreateMetricCard("DİSCORD DURUMU",
                out _discordValue, out _discordDetail));

        cards.Controls.Add(
            CreateMetricCard("NODE.JS RAM (RSS)",
                out _memoryValue, out _memoryDetail));

        cards.Controls.Add(
            CreateMetricCard("NODE.JS HEAP USED",
                out _heapValue, out _heapDetail));

        cards.Controls.Add(
            CreateMetricCard("PROCESS CPU",
                out _cpuValue, out _cpuDetail));

        cards.Controls.Add(
            CreateMetricCard("ÇALIŞMA SÜRESİ",
                out _uptimeValue, out _uptimeDetail));

        cards.Controls.Add(
            CreateMetricCard("WEBSOCKET PING",
                out _pingValue, out _pingDetail));

        void ResizeMetricCards()
        {
            int availableWidth = Math.Max(
                1,
                cards.ClientSize.Width -
                (cards.VerticalScroll.Visible
                    ? SystemInformation.VerticalScrollBarWidth
                    : 0));

            int columns = availableWidth >= 620
                ? 3
                : availableWidth >= 400 ? 2 : 1;

            int cardWidth = Math.Max(
                190,
                (availableWidth - (columns * 12) - 4) / columns);

            foreach (Control card in cards.Controls)
            {
                card.Width = cardWidth;

                Label? detailLabel =
                    card.Controls.OfType<Label>()
                        .FirstOrDefault(label =>
                            Equals(label.Tag, "detail"));

                if (detailLabel is not null)
                    detailLabel.Width = Math.Max(140, cardWidth - 30);
            }
        }

        cards.Resize += (_, _) => ResizeMetricCards();

        content.Controls.Add(cards, 0, 1);

        ResizeMetricCards();

        // ======================================
        // API STATUS
        // ======================================

        var status = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(14),
            Margin = new Padding(0, 4, 0, 8)
        };

        _apiInfo = new Label
        {
            Text = "API: henüz kontrol edilmedi",
            AutoSize = true,
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Location = new Point(14, 12)
        };

        _apiLatency = new Label
        {
            Text = "Yanıt süresi: —",
            AutoSize = true,
            ForeColor = Palette.Muted,
            Location = new Point(14, 42)
        };

        _lastUpdated = new Label
        {
            Text = "Son kontrol: —",
            AutoSize = true,
            ForeColor = Palette.Muted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(650, 27)
        };

        status.Controls.AddRange([
            _apiInfo,
            _apiLatency,
            _lastUpdated
        ]);

        status.Resize += (_, _) =>
        {
            _lastUpdated.Location = new Point(
                Math.Max(
                    300,
                    status.ClientSize.Width - _lastUpdated.Width - 14),
                27);
        };

        content.Controls.Add(status, 0, 2);

        // ======================================
        // HISTORY CHART
        // ======================================

        var chartPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(12),
            Margin = new Padding(0, 3, 0, 7)
        };

        var chartTitle = new Label
        {
            Text = "SON 60 ÖLÇÜM • CPU VE CONTAINER RAM GEÇMİŞİ",
            Dock = DockStyle.Top,
            Height = 25,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold)
        };

        _historyChart = new StatsHistoryChart
        {
            Dock = DockStyle.Fill
        };

        chartPanel.Controls.Add(_historyChart);
        chartPanel.Controls.Add(chartTitle);

        content.Controls.Add(chartPanel, 0, 3);

        // ======================================
        // MEMORY DETAILS
        // ======================================

        var detailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(13),
            Margin = new Padding(0, 2, 0, 7)
        };

        var detailsTitle = new Label
        {
            Text = "BELLEK VE ÇALIŞMA ZAMANI AYRINTILARI",
            Dock = DockStyle.Top,
            Height = 23,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold)
        };

        _containerMemoryDetail = new Label
        {
            Text = "Container RAM: API verisi bekleniyor",
            Dock = DockStyle.Top,
            Height = 25,
            ForeColor = Palette.Text,
            AutoEllipsis = true
        };

        _runtimeDetail = new Label
        {
            Text = "Node.js sürümü, komut sayısı ve çalışma ortamı bekleniyor",
            Dock = DockStyle.Fill,
            ForeColor = Palette.Muted,
            AutoEllipsis = true
        };

        detailsPanel.Controls.Add(_runtimeDetail);
        detailsPanel.Controls.Add(_containerMemoryDetail);
        detailsPanel.Controls.Add(detailsTitle);

        content.Controls.Add(detailsPanel, 0, 4);

        // ======================================
        // LIVE EVENTS PANEL
        // ======================================

        var livePanel = CreateLiveEventsPanel();

        content.Controls.Add(livePanel, 0, 5);

        // ======================================
        // RAW JSON
        // ======================================

        var rawPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(12)
        };

        var rawHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Palette.Surface
        };

        var rawTitle = new Label
        {
            Text = "API /api/stats HAM YANIT",
            Dock = DockStyle.Left,
            Width = 260,
            ForeColor = Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold)
        };

        var copyJson = CreateSmallButton("Kopyala", 82);
        copyJson.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        copyJson.Location = new Point(500, 0);
        copyJson.Click += (_, _) => CopyRawJson();

        var saveJson = CreateSmallButton("JSON Kaydet", 98);
        saveJson.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        saveJson.Location = new Point(590, 0);
        saveJson.Click += (_, _) => SaveRawJson();

        var openApi = CreateSmallButton("API'yi Aç", 92);
        openApi.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openApi.Location = new Point(696, 0);
        openApi.Click += (_, _) => OpenExternalUrl(_settings.BotApiUrl.TrimEnd('/') + "/api/stats");

        rawHeader.Controls.AddRange([rawTitle, copyJson, saveJson, openApi]);
        rawHeader.Resize += (_, _) =>
        {
            openApi.Location = new Point(Math.Max(0, rawHeader.ClientSize.Width - openApi.Width), 0);
            saveJson.Location = new Point(Math.Max(0, openApi.Left - saveJson.Width - 8), 0);
            copyJson.Location = new Point(Math.Max(0, saveJson.Left - copyJson.Width - 8), 0);
        };

        _rawJson = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.FromArgb(10, 14, 20),
            ForeColor = Color.FromArgb(170, 220, 180),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9F),
            Text = "İlk API kontrolü bekleniyor..."
        };

        rawPanel.Controls.Add(_rawJson);
        rawPanel.Controls.Add(rawHeader);

        content.Controls.Add(rawPanel, 0, 6);

        root.Controls.Add(content);

        return root;
    }

    // ==========================================
    // LIVE EVENTS UI
    // ==========================================

    private Panel CreateLiveEventsPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Surface,
            Padding = new Padding(12),
            Margin = new Padding(0, 3, 0, 7)
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Palette.Surface
        };

        var title = new Label
        {
            Text = "CANLI İSTEK AKIŞI",
            AutoSize = true,
            Location = new Point(0, 7),
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _liveStreamStatus = new Label
        {
            Text = "● BAĞLANTI BEKLENİYOR",
            AutoSize = true,
            ForeColor = Palette.Yellow,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(500, 8)
        };

        header.Controls.Add(title);
        header.Controls.Add(_liveStreamStatus);

        header.Resize += (_, _) =>
        {
            _liveStreamStatus.Location = new Point(
                Math.Max(
                    title.Right + 15,
                    header.ClientSize.Width - _liveStreamStatus.Width),
                8);
        };

        _liveEventCount = new Label
        {
            Text = "Gösterilen olay: 0",
            Dock = DockStyle.Bottom,
            Height = 21,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 8F),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _liveEventsGrid = new DataGridView
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

            ScrollBars = ScrollBars.Vertical
        };

        _liveEventsGrid.ColumnHeadersDefaultCellStyle.BackColor =
            Palette.SurfaceHover;

        _liveEventsGrid.ColumnHeadersDefaultCellStyle.ForeColor =
            Palette.Text;

        _liveEventsGrid.ColumnHeadersDefaultCellStyle.Font =
            new Font("Segoe UI", 8F, FontStyle.Bold);

        _liveEventsGrid.DefaultCellStyle.BackColor =
            Palette.Surface;

        _liveEventsGrid.DefaultCellStyle.ForeColor =
            Palette.Text;

        _liveEventsGrid.DefaultCellStyle.SelectionBackColor =
            Color.FromArgb(31, 48, 66);

        _liveEventsGrid.DefaultCellStyle.SelectionForeColor =
            Palette.Text;

        _liveEventsGrid.DefaultCellStyle.Padding =
            new Padding(6, 3, 6, 3);

        _liveEventsGrid.RowTemplate.Height = 29;

        _liveEventsGrid.Columns.Add("time", "ZAMAN");
        _liveEventsGrid.Columns.Add("source", "KAYNAK");
        _liveEventsGrid.Columns.Add("name", "İSTEK / KOMUT");
        _liveEventsGrid.Columns.Add("duration", "SÜRE");
        _liveEventsGrid.Columns.Add("status", "SONUÇ");

        _liveEventsGrid.Columns["time"]!.FillWeight = 17;
        _liveEventsGrid.Columns["source"]!.FillWeight = 18;
        _liveEventsGrid.Columns["name"]!.FillWeight = 39;
        _liveEventsGrid.Columns["duration"]!.FillWeight = 13;
        _liveEventsGrid.Columns["status"]!.FillWeight = 17;

        _liveEventsGrid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= _liveEventsGrid.Rows.Count)
                return;

            DataGridViewRow selected = _liveEventsGrid.Rows[args.RowIndex];
            string details = string.Join(Environment.NewLine, new[]
            {
                $"Zaman: {selected.Cells["time"].Value}",
                $"Kaynak: {selected.Cells["source"].Value}",
                $"İstek: {selected.Cells["name"].Value}",
                $"Süre: {selected.Cells["duration"].Value}",
                $"Sonuç: {selected.Cells["status"].Value}"
            });

            MessageBox.Show(details, "Canlı istek ayrıntısı",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var filterBar = CreateLiveEventToolbar();

        panel.Controls.Add(_liveEventsGrid);
        panel.Controls.Add(_liveEventCount);
        panel.Controls.Add(filterBar);
        panel.Controls.Add(header);

        return panel;
    }

    private void StartLiveStream()
    {
        if (IsDisposed || Disposing)
            return;

        _liveStreamCts?.Cancel();

        var source = new CancellationTokenSource();

        _liveStreamCts = source;

        _ = RunLiveStreamAsync(source);
    }

    private async Task RunLiveStreamAsync(
        CancellationTokenSource source)
    {
        CancellationToken cancellationToken = source.Token;

        int retrySeconds = 2;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   !IsDisposed &&
                   !Disposing)
            {
                try
                {
                    SetLiveStreamStatus(
                        "● CANLI AKIŞA BAĞLANILIYOR",
                        Palette.Yellow);

                    await _apiService.StreamEventsAsync(
                        _settings.BotApiUrl,
                        AddLiveEvent,
                        () =>
                        {
                            retrySeconds = 2;

                            SetLiveStreamStatus(
                                "● CANLI AKIŞ BAĞLI",
                                Palette.Green);
                        },
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetLiveStreamStatus(
                        "● BAĞLANTI KESİLDİ · YENİDEN DENENİYOR",
                        Palette.Yellow);

                    System.Diagnostics.Debug.WriteLine(
                        "[LIVE STREAM] " + ex.Message);
                }

                if (cancellationToken.IsCancellationRequested)
                    break;

                await Task.Delay(
                    TimeSpan.FromSeconds(retrySeconds),
                    cancellationToken);

                retrySeconds = Math.Min(retrySeconds * 2, 15);
            }
        }
        catch (OperationCanceledException)
        {
            // Beklenen bağlantı kapatma işlemi.
        }
        finally
        {
            if (ReferenceEquals(_liveStreamCts, source))
                _liveStreamCts = null;

            source.Dispose();
        }
    }

    private void SetLiveStreamStatus(
        string text,
        Color color)
    {
        if (IsDisposed || Disposing ||
            _liveStreamStatus is null ||
            !_liveStreamStatus.IsHandleCreated)
        {
            return;
        }

        if (_liveStreamStatus.InvokeRequired)
        {
            try
            {
                _liveStreamStatus.BeginInvoke(new Action(
                    () => SetLiveStreamStatus(text, color)));
            }
            catch (InvalidOperationException)
            {
                // Form kapanırken gelen bildirim.
            
        }

            return;
        }

        _liveStreamStatus.Text = text;
        _liveStreamStatus.ForeColor = color;
    }

    private void AddLiveEvent(LiveEvent liveEvent)
    {
        if (IsDisposed || Disposing ||
            _liveEventsGrid is null ||
            !_liveEventsGrid.IsHandleCreated)
        {
            return;
        }

        if (_liveEventsGrid.InvokeRequired)
        {
            try
            {
                _liveEventsGrid.BeginInvoke(new Action(
                    () => AddLiveEvent(liveEvent)));
            }
            catch (InvalidOperationException)
            {
                // Form kapanırken gelen bildirim.
            }

            return;
        }

        // SSE yeniden bağlanınca sunucu son 100 olayı tekrar yollar.
        // Aynı olayın iki kere görünmesini engelle.
        string eventKey =
            $"{liveEvent.Id}|" +
            $"{liveEvent.Timestamp?.ToUnixTimeMilliseconds() ?? 0}|" +
            $"{liveEvent.Type}|" +
            $"{liveEvent.Name}";

        if (!_liveEventKeys.Add(eventKey))
            return;

        _liveEventKeyOrder.Enqueue(eventKey);

        while (_liveEventKeyOrder.Count > MaxRememberedEventKeys)
        {
            string oldKey = _liveEventKeyOrder.Dequeue();
            _liveEventKeys.Remove(oldKey);
        }

        _liveEventHistory.Insert(0, liveEvent);
        while (_liveEventHistory.Count > MaximumStoredLiveEvents)
            _liveEventHistory.RemoveAt(_liveEventHistory.Count - 1);

        _liveEventCountValue++;

        if (!_liveEventsPaused)
            RenderLiveEvents();
        else if (_liveEventCount is not null)
            _liveEventCount.Text =
                $"Akış duraklatıldı • Bekleyen kayıt: {_liveEventHistory.Count} • Alınan toplam: {_liveEventCountValue}";
    }

    // ==========================================
    // METRIC CARD
    // ==========================================

    private Panel CreateMetricCard(
        string title,
        out Label value,
        out Label detail)
    {
        var card = new Panel
        {
            Width = 245,
            Height = 132,
            BackColor = Palette.Surface,
            Margin = new Padding(0, 0, 12, 10),
            Padding = new Padding(14)
        };

        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            Location = new Point(14, 13)
        };

        value = new Label
        {
            Text = "—",
            AutoSize = true,
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold),
            Location = new Point(14, 42)
        };

        detail = new Label
        {
            Text = "API verisi bekleniyor",
            AutoSize = false,
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 8F),
            Location = new Point(14, 99),
            Size = new Size(215, 26),
            AutoEllipsis = true,
            Tag = "detail"
        };

        card.Controls.AddRange([titleLabel, value, detail]);

        return card;
    }

    // ==========================================
    // WEB TERMINAL
    // ==========================================

    private Panel CreateTerminalPage()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background
        };

        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Palette.Surface,
            Padding = new Padding(10, 7, 10, 7)
        };

        _terminalState = new Label
        {
            Text = "Terminal hazırlanıyor...",
            AutoSize = true,
            ForeColor = Palette.Muted,
            Location = new Point(12, 15)
        };

        var reload = CreateSmallButton("Yenile", 82);
        var openBrowser = CreateSmallButton("Tarayıcıda Aç", 126);

        reload.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        reload.Click += (_, _) =>
        {
            if (_terminalWebView?.CoreWebView2 is not null)
                _terminalWebView.CoreWebView2.Reload();
            else
                _ = LoadTerminalAsync(forceReload: true);
        };

        openBrowser.Click += (_, _) => OpenExternalUrl(_settings.TerminalUrl);

        toolbar.Controls.Add(_terminalState);
        toolbar.Controls.Add(reload);
        toolbar.Controls.Add(openBrowser);

        toolbar.Resize += (_, _) =>
        {
            openBrowser.Location = new Point(
                Math.Max(8, toolbar.ClientSize.Width - openBrowser.Width - 8), 7);
            reload.Location = new Point(
                Math.Max(8, openBrowser.Left - reload.Width - 8), 7);
        };

        toolbar.PerformLayout();
        openBrowser.Location = new Point(Math.Max(8, toolbar.ClientSize.Width - openBrowser.Width - 8), 7);
        reload.Location = new Point(Math.Max(8, openBrowser.Left - reload.Width - 8), 7);

        _terminalWebView ??= new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Palette.Background
        };

        root.Controls.Add(_terminalWebView);
        root.Controls.Add(toolbar);

        return root;
    }

    private async Task LoadTerminalAsync(bool forceReload = false)
    {
        if (_terminalWebView is null ||
            _terminalWebView.IsDisposed)
        {
            return;
        }

        try
        {
            if (!_terminalInitialized)
            {
                _terminalState!.Text = "WebView2 başlatılıyor...";

                await _terminalWebView.EnsureCoreWebView2Async();

                _terminalWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _terminalWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

                _terminalWebView.CoreWebView2.NavigationCompleted +=
                    (_, args) =>
                    {
                        if (_terminalState is not null)
                        {
                            _terminalState.Text = args.IsSuccess
                                ? "● bot-site bağlantısı açık"
                                : "● sayfa yüklenemedi";

                            _terminalState.ForeColor = args.IsSuccess
                                ? Palette.Green
                                : Palette.Red;
                        }
                    };

                _terminalInitialized = true;
            }

            if (forceReload ||
                _terminalWebView.Source is null ||
                _terminalWebView.Source.ToString() != _settings.TerminalUrl)
            {
                _terminalWebView.CoreWebView2.Navigate(
                    _settings.TerminalUrl);
            }

            _terminalState!.Text =
                "bot-site adresine bağlanılıyor...";

            _terminalState.ForeColor = Palette.Yellow;
        }
        catch (Exception ex)
        {
            if (_terminalState is not null)
            {
                _terminalState.Text =
                    "WebView2 başlatılamadı: " + ex.Message;

                _terminalState.ForeColor = Palette.Red;
            }

            MessageBox.Show(
                "WebView2 başlatılamadı. Microsoft Edge WebView2 Runtime kurulu olmalı.\n\n" + ex.Message,
                "YTMT-STUDIO - WebView2",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // ==========================================
    // COMMAND CATALOG
    // ==========================================

    private Panel CreateCommandsPage()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            Padding = new Padding(0, 0, 0, 4)
        };

        var heading = new Label
        {
            Text = "Proje envanterinden gelen dokümantasyon kataloğu. Komutu kopyalamak için satıra çift tıkla; canlı dosya taraması değildir.",
            Dock = DockStyle.Top,
            Height = 42,
            ForeColor = Palette.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var search = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10F),
            PlaceholderText = "Komut veya açıklama ara..."
        };

        var grid = new DataGridView
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
            EnableHeadersVisualStyles = false
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = Palette.SurfaceHover;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        grid.DefaultCellStyle.BackColor = Palette.Surface;
        grid.DefaultCellStyle.ForeColor = Palette.Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(31, 48, 66);
        grid.DefaultCellStyle.SelectionForeColor = Palette.Text;
        grid.DefaultCellStyle.Padding = new Padding(7, 5, 7, 5);

        grid.RowTemplate.Height = 34;

        grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= grid.Rows.Count)
                return;

            string? commandText = grid.Rows[args.RowIndex].Cells["command"].Value?.ToString();
            if (!string.IsNullOrWhiteSpace(commandText))
            {
                try
                {
                    Clipboard.SetText(commandText);
                    _connectionBadge.Text = "●  KOMUT KOPYALANDI";
                    _connectionBadge.ForeColor = Palette.Blue;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Komut kopyalanamadı: " + ex.Message,
                        "Pano hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        };

        grid.Columns.Add("command", "Komut");
        grid.Columns.Add("description", "Açıklama");
        grid.Columns.Add("source", "Bilgi türü");

        grid.Columns[0].FillWeight = 28;
        grid.Columns[1].FillWeight = 58;
        grid.Columns[2].FillWeight = 18;

        var commands = GetDocumentedCommands();

        foreach (var command in commands)
        {
            grid.Rows.Add(
                "y!" + command.Name,
                command.Description,
                "Dokümantasyon");
        }

        search.TextChanged += (_, _) =>
        {
            string query = search.Text.Trim();

            foreach (DataGridViewRow row in grid.Rows)
            {
                string combined = string.Join(
                    " ",
                    row.Cells.Cast<DataGridViewCell>()
                        .Select(cell => cell.Value?.ToString()));

                row.Visible = combined.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase);
            }
        };

        root.Controls.Add(grid);
        root.Controls.Add(search);
        root.Controls.Add(heading);

        return root;
    }

    private static List<(string Name, string Description)> GetDocumentedCommands() =>
    [
        ("oto-rol", "Premium üyeliğe özel otomatik rol sistemi."),
        ("sa", "Selamlama mesajlarına cevap verir."),
        ("adamasmaca", "Kolay, orta ve zor seviyeli kelime oyunu."),
        ("afk", "AFK durumunu ve sebebini kaydeder."),
        ("ban-kurulum", "Banlanmış rolünü ve kanal izinlerini kurar."),
        ("ban", "Rol tabanlı erişim kısıtlaması uygular."),
        ("botbilgi", "Bot istatistiklerini ve ping değerini gösterir."),
        ("erensibot", "Eğlence amaçlı otomatik tepki komutu."),
        ("fullban", "Üyeyi Discord sunucusundan yasaklar."),
        ("haftalıkmsjsıralama", "Haftalık mesaj sıralamasını gösterir."),
        ("hgbb-kur", "Giriş-çıkış bildirim kanalını ayarlar."),
        ("kayıt-kurulum", "Kayıt rollerini ve kanal izinlerini kurar."),
        ("kayıt", "Üyeyi kayıtlı rolüne geçirir ve adını düzenler."),
        ("kick", "Belirtilen üyeyi sunucudan atar."),
        ("kurallar", "Kurallar panelini ve kabul butonunu gönderir."),
        ("link-engel", "Reklam ve davet bağlantısı filtresini yönetir."),
        ("logkur", "Mesaj, kanal ve rol olaylarını loglar."),
        ("mesajsayım", "Kullanıcının toplam mesaj sayısını gösterir."),
        ("ping", "Discord WebSocket gecikmesini ölçer."),
        ("premium-al", "Premium satın alma bağlantısını gönderir."),
        ("premium-ver", "Yetkili kullanıcıya premium tanımlar."),
        ("premium", "Sunucunun premium durumunu gösterir."),
        ("profil", "Kullanıcı ve sunucu katılım bilgilerini gösterir."),
        ("realrolkur", "Sunucu sahibine özel rol şablonunu kurar."),
        ("rolal", "Belirtilen rolü üyeden alır."),
        ("rolekle", "Yönetim ve rütbe rollerini oluşturur."),
        ("rolleri-sil", "Silinebilir özel rolleri temizler."),
        ("rolver", "Belirtilen rolü üyeye verir."),
        ("seviye", "Kullanıcının XP ve seviye durumunu gösterir."),
        ("sunucu", "Sunucu istatistiklerini gösterir."),
        ("sunucukur", "Kategori ve kanalları şablona göre kurar."),
        ("ticket-kur", "Butonlu destek talebi sistemini kurar."),
        ("unban", "Kullanıcının Discord yasağını kaldırır."),
        ("uyarı", "Kullanıcıya uyarı puanı ekler."),
        ("uyarıkaldır", "Kullanıcının uyarılarını azaltır veya siler."),
        ("uyarılar", "Kullanıcının uyarı geçmişini gösterir."),
        ("veriler", "Bot sahibine JSON veri dosyalarını gönderir."),
        ("yardım", "Komut kategorileri ve sayfalama paneli sunar."),
        ("yt-bildirim", "YouTube kanal duyurularını ayarlar."),
        ("zarat", "Bir ile altı arasında zar atar.")
    ];

    // ==========================================
    // SETTINGS
    // ==========================================

    private Panel CreateSettingsPage()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            AutoScroll = true
        };

        var card = new Panel
        {
            BackColor = Palette.Surface,
            Location = new Point(0, 8),
            Size = new Size(760, 390),
            Padding = new Padding(22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        root.Controls.Add(card);

        root.Resize += (_, _) =>
        {
            card.Width = Math.Max(500, root.ClientSize.Width - 8);
        };

        var title = new Label
        {
            Text = "Bağlantı ve yenileme",
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            Location = new Point(22, 18),
            AutoSize = true
        };

        var apiLabel = MakeSettingLabel(
            "Ana bot API adresi",
            22,
            65);

        var apiInput = MakeSettingInput(
            _settings.BotApiUrl,
            22,
            88,
            690);

        var terminalLabel = MakeSettingLabel(
            "Web terminal adresi",
            22,
            142);

        var terminalInput = MakeSettingInput(
            _settings.TerminalUrl,
            22,
            165,
            690);

        var refreshLabel = MakeSettingLabel(
            "Otomatik istatistik yenileme aralığı (saniye, 5–300)",
            22,
            220);

        var refreshInput = new NumericUpDown
        {
            Minimum = 5,
            Maximum = 300,
            Value = Math.Clamp(
                _settings.RefreshIntervalSeconds,
                5,
                300),
            Location = new Point(22, 244),
            Width = 140,
            BackColor = Palette.SurfaceHover,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10F)
        };

        var save = CreateSmallButton(
            "Ayarları Kaydet",
            150);

        save.Location = new Point(22, 305);

        save.Click += (_, _) =>
        {
            try
            {
                string apiUrl =
                    ApiService.NormalizeBaseUrl(apiInput.Text);

                string terminalUrl =
                    ApiService.NormalizeBaseUrl(terminalInput.Text);

                _settings = new AppSettings
                {
                    BotApiUrl = apiUrl.TrimEnd('/'),
                    TerminalUrl = terminalUrl.TrimEnd('/'),
                    RefreshIntervalSeconds = (int)refreshInput.Value,
                    RenderServiceId = _settings.RenderServiceId
                };

                _settingsService.Save(_settings);

                _refreshTimer.Interval =
                    _settings.RefreshIntervalSeconds * 1000;

                _terminalInitialized = false;

                // Sayfayı önce kapat; WebView2 alt kontrolü de güvenli biçimde dispose edilir.
                _terminalPage?.Dispose();
                _terminalPage = null;
                _terminalWebView = null;

                // Yeni API adresi varsa canlı akışı da yeniden bağla.
                StartLiveStream();

                MessageBox.Show(
                    "Ayarlar kaydedildi.",
                    "YTMT-STUDIO",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                _ = RefreshDashboardAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Ayar doğrulama hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        };

        var test = CreateSmallButton(
            "Bağlantıyı Test Et",
            150);

        test.Location = new Point(184, 305);

        test.Click += async (_, _) =>
        {
            try
            {
                string candidateUrl = ApiService.NormalizeBaseUrl(apiInput.Text);
                ApiResult testResult = await _apiService.GetStatsAsync(candidateUrl);

                string message = testResult.Success
                    ? $"Bağlantı başarılı.\nHTTP: {testResult.StatusCode}\nYanıt: {testResult.Elapsed.TotalMilliseconds:0} ms\n\nBu adres henüz kaydedilmedi."
                    : $"Bağlantı başarısız.\n{testResult.Error ?? "Bilinmeyen hata"}\n\nBu adres henüz kaydedilmedi.";

                MessageBox.Show(message, "API bağlantı testi",
                    MessageBoxButtons.OK,
                    testResult.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Geçersiz API adresi",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        var note = new Label
        {
            Text = "Not: İstatistikler /api/stats adresinden, canlı istekler /api/events/stream adresinden alınır. Web terminal bot-site adresini WebView2 içinde açar.",
            ForeColor = Palette.Muted,
            Location = new Point(22, 350),
            Size = new Size(690, 38)
        };

        card.Controls.AddRange([
            title,
            apiLabel,
            apiInput,
            terminalLabel,
            terminalInput,
            refreshLabel,
            refreshInput,
            save,
            test,
            note
        ]);

        return root;
    }

    private static Label MakeSettingLabel(
        string text,
        int x,
        int y)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Palette.Muted,
            Location = new Point(x, y),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
    }

    private static TextBox MakeSettingInput(
        string value,
        int x,
        int y,
        int width)
    {
        return new TextBox
        {
            Text = value,
            Location = new Point(x, y),
            Width = width,
            Height = 32,
            BackColor = Palette.SurfaceHover,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5F)
        };
    }

    private static Button CreateSmallButton(
        string text,
        int width)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 93, 151),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderSize = 0;

        button.FlatAppearance.MouseOverBackColor =
            Color.FromArgb(48, 111, 173);

        return button;
    }

    // ==========================================
    // REFRESH DASHBOARD
    // ==========================================

    private async Task RefreshDashboardAsync()
    {
        if (IsDisposed ||
            !await _refreshLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            _connectionBadge.Text = "●  API KONTROL EDİLİYOR";
            _connectionBadge.ForeColor = Palette.Yellow;

            ApiResult result = await _apiService.GetStatsAsync(
                _settings.BotApiUrl);

            if (IsDisposed || Disposing)
                return;

            RecordApiCheck(result);

            if (result.Success)
            {
                _connectionBadge.Text = "●  API ERİŞİLEBİLİR";
                _connectionBadge.ForeColor = Palette.Green;

                _apiInfo.Text =
                    $"API DURUMU: ERİŞİLEBİLİR  •  HTTP {result.StatusCode}";

                _apiInfo.ForeColor = Palette.Green;

                _apiLatency.Text =
                    $"API yanıt süresi: {result.Elapsed.TotalMilliseconds:0} ms";

                ApplyStats(result.Stats);

                _rawJson.Text = FormatJson(result.RawJson);
            }
            else
            {
                _connectionBadge.Text = "●  API BAĞLANTISI YOK";
                _connectionBadge.ForeColor = Palette.Red;

                _apiInfo.Text =
                    "API DURUMU: BAĞLANTI KURULAMADI";

                _apiInfo.ForeColor = Palette.Red;

                _apiLatency.Text =
                    result.Error ?? "Bilinmeyen bağlantı hatası.";

                _rawJson.Text =
                    string.IsNullOrWhiteSpace(result.RawJson)
                        ? result.Error ?? "Yanıt alınamadı."
                        : result.RawJson;

                SetUnknownStats();
            }

            _lastUpdated.Text =
                "Son kontrol: " +
                DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

            if (_lastUpdated.Parent is not null)
            {
                _lastUpdated.Location = new Point(
                    Math.Max(
                        300,
                        _lastUpdated.Parent.ClientSize.Width -
                        _lastUpdated.Width - 14),
                    27);
            }
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing)
                return;

            _connectionBadge.Text = "●  KONTROL HATASI";
            _connectionBadge.ForeColor = Palette.Red;

            _apiInfo.Text = "API kontrol hatası: " + ex.Message;
            _apiInfo.ForeColor = Palette.Red;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // ==========================================
    // APPLY STATS
    // ==========================================

    private void ApplyStats(BotStats? stats)
    {
        if (stats is null)
        {
            SetUnknownStats();
            return;
        }

        _discordValue.Text = stats.DiscordOnline switch
        {
            true => "ONLINE",
            false => "OFFLINE",
            _ => "BİLİNMİYOR"
        };

        _discordValue.ForeColor = stats.DiscordOnline switch
        {
            true => Palette.Green,
            false => Palette.Red,
            _ => Palette.Text
        };

        _discordDetail.Text = stats.DiscordOnline.HasValue
            ? "Discord bağlantı durumu"
            : "API bu alanı henüz sunmuyor";

        _memoryValue.Text = stats.NodeRssBytes.HasValue
            ? FormatBytes(stats.NodeRssBytes.Value)
            : "—";

        _memoryDetail.Text = stats.NodeRssBytes.HasValue
            ? "Node.js sürecinin toplam RSS belleği"
            : "RSS alanı API yanıtında yok";

        _heapValue.Text = stats.HeapUsedBytes.HasValue
            ? FormatBytes(stats.HeapUsedBytes.Value)
            : "—";

        var heapDetails = new List<string>();

        if (stats.HeapTotalBytes.HasValue)
        {
            heapDetails.Add(
                $"Ayırılan {FormatBytes(stats.HeapTotalBytes.Value)}");
        }

        if (stats.HeapTotalBytes.HasValue &&
            stats.HeapUsedBytes.HasValue &&
            stats.HeapTotalBytes.Value >= stats.HeapUsedBytes.Value)
        {
            heapDetails.Add(
                $"Heap boş {FormatBytes(stats.HeapTotalBytes.Value - stats.HeapUsedBytes.Value)}");
        }

        if (stats.ExternalBytes.HasValue)
        {
            heapDetails.Add(
                $"External {FormatBytes(stats.ExternalBytes.Value)}");
        }

        if (stats.ArrayBuffersBytes.HasValue)
        {
            heapDetails.Add(
                $"ArrayBuffers {FormatBytes(stats.ArrayBuffersBytes.Value)}");
        }

        _heapDetail.Text = heapDetails.Count > 0
            ? string.Join(" • ", heapDetails)
            : "Heap ayrıntısı API yanıtında yok";

        double? processCpu =
            stats.ProcessCpuPercent ?? stats.HostCpuPercent;

        _cpuValue.Text = processCpu.HasValue
            ? $"{processCpu.Value:0.00}%"
            : "—";

        _cpuDetail.Text = stats.ProcessCpuPercent.HasValue
            ? "Node.js sürecinin CPU payı"
            : stats.HostCpuPercent.HasValue
                ? "Host işletim sistemi CPU kullanımı"
                : "CPU metriği API yanıtında yok";

        _uptimeValue.Text = stats.UptimeSeconds.HasValue
            ? FormatDuration(stats.UptimeSeconds.Value)
            : "—";

        _uptimeDetail.Text = stats.UptimeSeconds.HasValue
            ? "Node.js süreç çalışma süresi"
            : "Uptime alanı API yanıtında yok";

        _pingValue.Text = stats.WebSocketPingMs.HasValue
            ? $"{stats.WebSocketPingMs.Value:0} ms"
            : "—";

        _pingDetail.Text = stats.WebSocketPingMs.HasValue
            ? "Discord WebSocket gecikmesi"
            : "WebSocket ping alanı API yanıtında yok";

        if (stats.ContainerMemoryUsedBytes.HasValue &&
            stats.ContainerMemoryLimitBytes is > 0)
        {
            string percent = stats.ContainerMemoryPercent.HasValue
                ? $" • %{stats.ContainerMemoryPercent.Value:0.0}"
                : string.Empty;

            long freeBytes = Math.Max(
                0,
                stats.ContainerMemoryLimitBytes.Value -
                stats.ContainerMemoryUsedBytes.Value);

            _containerMemoryDetail.Text =
                $"Container RAM: {FormatBytes(stats.ContainerMemoryUsedBytes.Value)} / " +
                $"{FormatBytes(stats.ContainerMemoryLimitBytes.Value)}" +
                $"{percent} • Boş {FormatBytes(freeBytes)}";
        }
        else if (stats.ContainerMemoryUsedBytes.HasValue)
        {
            _containerMemoryDetail.Text =
                $"Container RAM kullanımı: {FormatBytes(stats.ContainerMemoryUsedBytes.Value)} • limit bilgisi yok";
        }
        else
        {
            _containerMemoryDetail.Text =
                "Container RAM: cgroup ölçümü API yanıtında yok; Node RSS ayrı gösterilir";
        }

        var runtimeDetails = new List<string>();

        if (!string.IsNullOrWhiteSpace(stats.NodeVersion))
            runtimeDetails.Add(stats.NodeVersion);

        if (!string.IsNullOrWhiteSpace(stats.Platform))
            runtimeDetails.Add(stats.Platform);

        if (!string.IsNullOrWhiteSpace(stats.Architecture))
            runtimeDetails.Add(stats.Architecture);

        if (stats.ProcessId.HasValue)
            runtimeDetails.Add($"PID {stats.ProcessId.Value}");

        if (stats.CpuCoreCount.HasValue)
            runtimeDetails.Add($"{stats.CpuCoreCount.Value} CPU çekirdeği");

        if (stats.LoadAverage1m.HasValue)
            runtimeDetails.Add($"Load 1m {stats.LoadAverage1m.Value:0.00}");

        if (stats.CommandCount.HasValue)
            runtimeDetails.Add($"{stats.CommandCount.Value} komut");

        _runtimeDetail.Text = runtimeDetails.Count > 0
            ? string.Join("  •  ", runtimeDetails)
            : "Node.js sürümü, platform, PID ve komut sayısı API yanıtında yok";

        _historyChart?.AddSample(
            processCpu,
            stats.ContainerMemoryPercent);
    }

    private void SetUnknownStats()
    {
        _discordValue.Text = "—";
        _discordValue.ForeColor = Palette.Text;
        _discordDetail.Text = "Bağlantı verisi yok";

        _memoryValue.Text = "—";
        _memoryDetail.Text = "API verisi yok";

        _heapValue.Text = "—";
        _heapDetail.Text = "API verisi yok";

        _cpuValue.Text = "—";
        _cpuDetail.Text = "API verisi yok";

        _containerMemoryDetail.Text =
            "Container RAM: API verisi yok";

        _runtimeDetail.Text =
            "Çalışma ortamı verisi yok";

        _uptimeValue.Text = "—";
        _uptimeDetail.Text = "API verisi yok";

        _pingValue.Text = "—";
        _pingDetail.Text = "API verisi yok";
    }

    // ==========================================
    // HELPERS
    // ==========================================

    private static string FormatJson(string json)
    {
        try
        {
            using JsonDocument document =
                JsonDocument.Parse(json);

            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
        }
        catch
        {
            return json;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds < 0 ||
            double.IsNaN(seconds) ||
            double.IsInfinity(seconds))
        {
            return "—";
        }

        var duration = TimeSpan.FromSeconds(
            Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}g {duration.Hours}sa {duration.Minutes}dk";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}sa {duration.Minutes}dk";
        }

        return $"{duration.Minutes}dk {duration.Seconds}sn";
    }
}