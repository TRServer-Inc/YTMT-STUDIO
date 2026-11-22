using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using YTMT_STUDIO.Models;
using YTMT_STUDIO.Services;

namespace YTMT_STUDIO;

public sealed partial class MainForm
{
    private readonly GuildWorkspaceStore _guildWorkspaceStore = new();
    private DiscordOAuthService? _discordOAuth;
    private DiscordSession? _discordSession;
    private DiscordGuildInfo? _selectedDiscordGuild;
    private DiscordGuildBotStatus? _selectedBotStatus;
    private DiscordGuildResources _guildResources = new();
    private GuildWorkspace? _activeGuildWorkspace;
    private List<DiscordGuildInfo> _manageableGuilds = [];
    private int _guildSelectionVersion;

    private Panel? _serverManagementPage;
    private DataGridView? _serverGuildGrid;
    private TextBox? _discordClientIdBox;
    private Label? _discordConnectionStatus;
    private Label? _selectedGuildCaption;
    private Label? _selectedBotCaption;
    private Label? _serverPermissionCaption;
    private CheckedListBox? _autoRoleList;
    private CheckBox? _welcomeEnabled;
    private ComboBox? _welcomeChannel;
    private TextBox? _welcomeMessage;
    private ComboBox? _logChannel;
    private CheckBox? _antiSpamEnabled;
    private CheckBox? _capsFilterEnabled;
    private CheckBox? _profanityFilterEnabled;
    private ComboBox? _reactionMode;
    private ComboBox? _reactionChannel;
    private TextBox? _reactionMessageId;
    private TextBox? _reactionMessageContent;
    private DataGridView? _reactionMappingGrid;
    private ListBox? _reactionDraftList;
    private Label? _draftStatus;

    private Panel CreateServerManagementPage()
    {
        if (_serverManagementPage is not null && !_serverManagementPage.IsDisposed)
            return _serverManagementPage;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            Padding = new Padding(12)
        };

        var top = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Palette.Background
        };

        var title = new Label
        {
            Text = "Sunucu Yönetim Merkezi",
            Font = new Font("Segoe UI Semibold", 17, FontStyle.Bold),
            ForeColor = Palette.Text,
            Location = new Point(4, 2),
            AutoSize = true
        };
        var subtitle = new Label
        {
            Text = "Discord hesabını bağla, yönetebildiğin sunucuları seç ve güvenli yapılandırma taslakları oluştur.",
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Palette.Muted,
            Location = new Point(6, 34),
            AutoSize = true
        };

        var clientLabel = MakeStudioLabel("Application / Client ID", new Point(6, 67));
        _discordClientIdBox = new TextBox
        {
            Location = new Point(154, 64),
            Size = new Size(230, 28),
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = _settings.DiscordClientId
        };
        var saveClientIdButton = MakeStudioButton("Client ID Kaydet", new Point(392, 62), 122);
        saveClientIdButton.Click += (_, _) => SaveDiscordClientId();

        var connectButton = MakeStudioButton("Discord'a Bağlan", new Point(526, 62), 142, primary: true);
        connectButton.Click += async (_, _) => await ConnectDiscordAsync();

        var refreshButton = MakeStudioButton("Sunucuları Yenile", new Point(676, 62), 142);
        refreshButton.Click += async (_, _) => await RefreshDiscordGuildsAsync();

        _discordConnectionStatus = MakeStudioLabel("Discord bağlantısı bekleniyor.", new Point(830, 68));
        _discordConnectionStatus.ForeColor = Palette.Yellow;

        top.Controls.AddRange([title, subtitle, clientLabel, _discordClientIdBox,
            saveClientIdButton, connectButton, refreshButton, _discordConnectionStatus]);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Border,
            BorderStyle = BorderStyle.FixedSingle
        };
        split.SizeChanged += (_, _) =>
        {
            if (split.Width > 790)
            {
                split.Panel1MinSize = 240;
                split.Panel2MinSize = 520;
                if (split.SplitterDistance < 280)
                    split.SplitterDistance = 300;
            }
        };

        var left = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Surface, Padding = new Padding(10) };
        var serverHeader = MakeStudioLabel("YÖNETEBİLDİĞİN SUNUCULAR", new Point(8, 8));
        serverHeader.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        serverHeader.ForeColor = Palette.Muted;

        _serverGuildGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            Location = new Point(0, 32),
            BackgroundColor = Palette.Surface,
            BorderStyle = BorderStyle.None,
            GridColor = Palette.Border,
            ForeColor = Palette.Text,
            EnableHeadersVisualStyles = false,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AutoGenerateColumns = false
        };
        _serverGuildGrid.ColumnHeadersDefaultCellStyle.BackColor = Palette.Sidebar;
        _serverGuildGrid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Muted;
        _serverGuildGrid.DefaultCellStyle.BackColor = Palette.Surface;
        _serverGuildGrid.DefaultCellStyle.ForeColor = Palette.Text;
        _serverGuildGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(35, 65, 94);
        _serverGuildGrid.DefaultCellStyle.SelectionForeColor = Palette.Text;
        _serverGuildGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sunucu", DataPropertyName = "Name", FillWeight = 62 });
        _serverGuildGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Üye", DataPropertyName = "MemberCount", FillWeight = 18 });
        _serverGuildGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Yetki", DataPropertyName = "PermissionSummary", FillWeight = 40 });
        _serverGuildGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", DataPropertyName = "Id", Visible = false });
        _serverGuildGrid.SelectionChanged += async (_, _) => await OnGuildSelectionChangedAsync();

        left.Controls.Add(_serverGuildGrid);
        left.Controls.Add(serverHeader);
        serverHeader.BringToFront();
        split.Panel1.Controls.Add(left);

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Background, Padding = new Padding(8) };
        var summary = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Palette.Surface, Padding = new Padding(10) };
        _selectedGuildCaption = MakeStudioLabel("Bir sunucu seç.", new Point(4, 3));
        _selectedGuildCaption.Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold);
        _selectedBotCaption = MakeStudioLabel("YTMT BOT: kontrol edilmedi", new Point(4, 30));
        _selectedBotCaption.ForeColor = Palette.Yellow;
        _serverPermissionCaption = MakeStudioLabel("Discord OAuth2 bağlantısı olmadan sunucu ayrıntıları gösterilmez.", new Point(360, 32));
        _serverPermissionCaption.ForeColor = Palette.Muted;
        var inviteButton = MakeStudioButton("Botu Sunucuya Ekle", new Point(0, 0), 156, primary: true);
        inviteButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        inviteButton.Location = new Point(840, 16);
        summary.SizeChanged += (_, _) => inviteButton.Location = new Point(Math.Max(8, summary.ClientSize.Width - inviteButton.Width - 12), 16);
        inviteButton.Click += (_, _) => OpenBotInvite();
        summary.Controls.AddRange([_selectedGuildCaption, _selectedBotCaption, _serverPermissionCaption, inviteButton]);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.Normal,
            Padding = new Point(14, 5)
        };
        tabs.TabPages.Add(BuildAutoRoleTab());
        tabs.TabPages.Add(BuildReactionRoleTab());
        tabs.TabPages.Add(BuildWelcomeTab());
        tabs.TabPages.Add(BuildStudioToolsTab());

        right.Controls.Add(tabs);
        right.Controls.Add(summary);
        split.Panel2.Controls.Add(right);

        root.Controls.Add(split);
        root.Controls.Add(top);
        _serverManagementPage = root;

        _ = LoadSavedDiscordSessionAsync();
        return root;
    }

    private TabPage BuildAutoRoleTab()
    {
        var tab = MakeTab("Oto Rol");
        var hint = MakeStudioLabel(
            "Sunucuya yeni katılan üyelere verilecek roller. Yalnızca botun rol hiyerarşisinde düzenleyebildiği roller listelenir.",
            new Point(14, 14));
        hint.MaximumSize = new Size(850, 40);
        _autoRoleList = new CheckedListBox
        {
            Location = new Point(14, 58),
            Size = new Size(610, 330),
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            CheckOnClick = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        var note = MakeStudioLabel(
            "Not: Bu ekrandaki değişiklikler yerel taslakta tutulur. Canlı bota uygulanmış sayılmaz.",
            new Point(14, 397));
        note.ForeColor = Palette.Yellow;
        tab.Controls.AddRange([hint, _autoRoleList, note]);
        return tab;
    }

    private TabPage BuildReactionRoleTab()
    {
        var tab = MakeTab("Mesaj Rol");
        var instructions = MakeStudioLabel(
            "Mevcut mesajın ID'sine tepki bağla veya botun daha sonra göndereceği yeni bir mesaj tasarla.",
            new Point(14, 12));
        instructions.MaximumSize = new Size(860, 36);

        var modeLabel = MakeStudioLabel("Yöntem", new Point(14, 52));
        _reactionMode = new ComboBox
        {
            Location = new Point(14, 75),
            Width = 235,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _reactionMode.Items.AddRange(["Mevcut mesaj ID'si", "Yeni mesaj oluştur"]);
        _reactionMode.SelectedIndex = 0;
        _reactionMode.SelectedIndexChanged += (_, _) => UpdateReactionModeControls();

        var channelLabel = MakeStudioLabel("Kanal", new Point(270, 52));
        _reactionChannel = new ComboBox
        {
            Location = new Point(270, 75),
            Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        var messageIdLabel = MakeStudioLabel("Mevcut mesaj ID'si", new Point(14, 112));
        _reactionMessageId = new TextBox
        {
            Location = new Point(14, 135),
            Width = 340,
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle
        };

        var contentLabel = MakeStudioLabel("Yeni mesaj metni", new Point(14, 112));
        contentLabel.Name = "reactionContentLabel";
        contentLabel.Visible = false;
        _reactionMessageContent = new TextBox
        {
            Location = new Point(14, 135),
            Width = 520,
            Height = 66,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var mappingTitle = MakeStudioLabel("Emoji → Rol eşleştirmeleri", new Point(14, 176));
        _reactionMappingGrid = new DataGridView
        {
            Location = new Point(14, 199),
            Size = new Size(520, 150),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackgroundColor = Palette.Surface,
            ForeColor = Palette.Text,
            GridColor = Palette.Border,
            BorderStyle = BorderStyle.FixedSingle,
            EnableHeadersVisualStyles = false,
            RowHeadersVisible = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        _reactionMappingGrid.ColumnHeadersDefaultCellStyle.BackColor = Palette.Sidebar;
        _reactionMappingGrid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Muted;
        _reactionMappingGrid.DefaultCellStyle.BackColor = Palette.Surface;
        _reactionMappingGrid.DefaultCellStyle.ForeColor = Palette.Text;
        _reactionMappingGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Emoji", Name = "Emoji", FillWeight = 25 });
        _reactionMappingGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rol ID", Name = "RoleId", FillWeight = 75 });

        var addButton = MakeStudioButton("Eşleştirmeyi Taslağa Ekle", new Point(550, 199), 190, primary: true);
        addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        addButton.Click += (_, _) => AddReactionRoleDraft();

        _reactionDraftList = new ListBox
        {
            Location = new Point(550, 238),
            Size = new Size(270, 110),
            Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var removeButton = MakeStudioButton("Seçili taslağı kaldır", new Point(550, 356), 190);
        removeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        removeButton.Click += (_, _) => RemoveReactionRoleDraft();

        tab.Controls.AddRange([instructions, modeLabel, _reactionMode, channelLabel, _reactionChannel,
            messageIdLabel, _reactionMessageId, contentLabel, _reactionMessageContent, mappingTitle,
            _reactionMappingGrid, addButton, _reactionDraftList, removeButton]);
        return tab;
    }

    private TabPage BuildWelcomeTab()
    {
        var tab = MakeTab("Karşılama ve Moderasyon");
        _welcomeEnabled = new CheckBox
        {
            Text = "Yeni üye karşılama mesajını etkinleştir (taslak)",
            Location = new Point(14, 16),
            AutoSize = true,
            ForeColor = Palette.Text
        };
        var channelLabel = MakeStudioLabel("Karşılama kanalı", new Point(14, 52));
        _welcomeChannel = new ComboBox
        {
            Location = new Point(14, 76),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        var messageLabel = MakeStudioLabel("Karşılama mesajı", new Point(14, 112));
        _welcomeMessage = new TextBox
        {
            Location = new Point(14, 136),
            Width = 590,
            Height = 70,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Palette.Surface,
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        var placeholders = MakeStudioLabel("Değişkenler: {user}, {username}, {server}, {memberCount}", new Point(14, 212));
        placeholders.ForeColor = Palette.Muted;
        var logLabel = MakeStudioLabel("Log kanalı (isteğe bağlı)", new Point(14, 250));
        _logChannel = new ComboBox
        {
            Location = new Point(14, 274),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        _antiSpamEnabled = MakeStudioCheck("Spam filtresi", new Point(14, 330));
        _capsFilterEnabled = MakeStudioCheck("Büyük harf filtresi", new Point(170, 330));
        _profanityFilterEnabled = MakeStudioCheck("Küfür filtresi", new Point(360, 330));

        var warning = MakeStudioLabel(
            "Bu anahtarlar yalnızca dışa aktarılan taslakta saklanır; mevcut botun çalışan ayarları değiştirilmez.",
            new Point(14, 375));
        warning.ForeColor = Palette.Yellow;
        warning.MaximumSize = new Size(800, 42);
        tab.Controls.AddRange([_welcomeEnabled, channelLabel, _welcomeChannel, messageLabel,
            _welcomeMessage, placeholders, logLabel, _logChannel, _antiSpamEnabled,
            _capsFilterEnabled, _profanityFilterEnabled, warning]);
        return tab;
    }

    private TabPage BuildStudioToolsTab()
    {
        var tab = MakeTab("Araçlar ve Kayıtlar");
        var save = MakeStudioButton("Taslağı Kaydet", new Point(16, 16), 160, primary: true);
        save.Click += (_, _) => SaveGuildWorkspace();
        var validate = MakeStudioButton("Yapılandırmayı Doğrula", new Point(188, 16), 188);
        validate.Click += (_, _) => ValidateGuildWorkspace();
        var export = MakeStudioButton("JSON Dışa Aktar", new Point(388, 16), 160);
        export.Click += (_, _) => ExportGuildWorkspace();
        var import = MakeStudioButton("JSON İçe Aktar", new Point(560, 16), 160);
        import.Click += (_, _) => ImportGuildWorkspace();
        var audit = MakeStudioButton("Yerel Kayıtları Aç", new Point(16, 66), 180);
        audit.Click += (_, _) => OpenGuildAuditLog();
        var copy = MakeStudioButton("Sunucu ID Kopyala", new Point(210, 66), 180);
        copy.Click += (_, _) =>
        {
            if (_selectedDiscordGuild is not null)
                Clipboard.SetText(_selectedDiscordGuild.Id);
        };
        _draftStatus = MakeStudioLabel("Henüz sunucu seçilmedi.", new Point(16, 122));
        _draftStatus.ForeColor = Palette.Muted;
        var info = MakeStudioLabel(
            "Güvenlik: Discord erişim ve yenileme token'ları Windows DPAPI CurrentUser ile şifrelenir. Taslak JSON dosyaları token içermez. Canlı bot ayarlarına yazma işlemi bu sürümde özellikle kapalıdır.",
            new Point(16, 158));
        info.MaximumSize = new Size(820, 95);
        info.ForeColor = Palette.Muted;
        tab.Controls.AddRange([save, validate, export, import, audit, copy, _draftStatus, info]);
        return tab;
    }

    private async Task LoadSavedDiscordSessionAsync()
    {
        try
        {
            if (_discordClientIdBox is null || string.IsNullOrWhiteSpace(_settings.DiscordClientId))
                return;

            _discordOAuth ??= new DiscordOAuthService(_settings.DiscordClientId, _settings.BotApiUrl);
            _discordSession = await _discordOAuth.LoadSavedSessionAsync();
            if (_discordSession is not null)
            {
                SetDiscordConnectionText($"Bağlandı: {_discordSession.GlobalName} (@{_discordSession.Username})", Palette.Green);
                await RefreshDiscordGuildsAsync();
            }
        }
        catch (Exception ex)
        {
            SetDiscordConnectionText("Oturum yüklenemedi: " + ex.Message, Palette.Red);
        }
    }

    private void SaveDiscordClientId()
    {
        string value = _discordClientIdBox?.Text.Trim() ?? string.Empty;
        if (!ulong.TryParse(value, out _))
        {
            MessageBox.Show(this, "Discord Application / Client ID sayısal olmalıdır.",
                "Client ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.DiscordClientId = value;
        _settingsService.Save(_settings);
        _discordOAuth?.Dispose();
        _discordOAuth = new DiscordOAuthService(_settings.DiscordClientId, _settings.BotApiUrl);
        SetDiscordConnectionText("Client ID kaydedildi. Discord'a bağlanabilirsin.", Palette.Green);
    }

    private async Task ConnectDiscordAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.DiscordClientId))
        {
            MessageBox.Show(this, "Önce Discord Application / Client ID değerini kaydet.",
                "Discord ayarı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            _discordOAuth ??= new DiscordOAuthService(_settings.DiscordClientId, _settings.BotApiUrl);
            SetDiscordConnectionText("Tarayıcıda Discord onayı bekleniyor...", Palette.Yellow);
            _discordSession = await _discordOAuth.SignInAsync();
            SetDiscordConnectionText($"Bağlandı: {_discordSession.GlobalName} (@{_discordSession.Username})", Palette.Green);
            _guildWorkspaceStore.AppendAudit("0", "Discord", "discord_connected", "Discord hesabı bağlandı.");
            await RefreshDiscordGuildsAsync();
        }
        catch (Exception ex)
        {
            SetDiscordConnectionText(ex.Message, Palette.Red);
            MessageBox.Show(this, ex.Message, "Discord bağlantısı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshDiscordGuildsAsync()
    {
        if (_discordSession is null)
        {
            SetDiscordConnectionText("Önce Discord hesabını bağla.", Palette.Yellow);
            return;
        }

        try
        {
            _discordOAuth ??= new DiscordOAuthService(_settings.DiscordClientId, _settings.BotApiUrl);
            _manageableGuilds = await _discordOAuth.GetManageableGuildsAsync(_discordSession);
            if (_serverGuildGrid is null) return;

            _serverGuildGrid.DataSource = _manageableGuilds.Select(g => new GuildGridRow
            {
                Id = g.Id,
                Name = g.Name,
                MemberCount = g.MemberCount?.ToString("N0") ?? "—",
                PermissionSummary = g.Owner ? "Sahip" :
                    g.IsAdministrator ? "Yönetici" :
                    g.CanManageGuild ? "Sunucu" :
                    g.CanManageRoles ? "Rol" : "—"
            }).ToList();

            SetDiscordConnectionText($"{_manageableGuilds.Count} yönetilebilir sunucu bulundu.", Palette.Green);
            if (_serverGuildGrid.Rows.Count > 0 && _serverGuildGrid.SelectedRows.Count == 0)
                _serverGuildGrid.Rows[0].Selected = true;
        }
        catch (Exception ex)
        {
            SetDiscordConnectionText("Sunucu listesi alınamadı: " + ex.Message, Palette.Red);
        }
    }

    private async Task OnGuildSelectionChangedAsync()
    {
        if (_serverGuildGrid?.CurrentRow?.DataBoundItem is not GuildGridRow row)
            return;

        DiscordGuildInfo? guild = _manageableGuilds.FirstOrDefault(g => g.Id == row.Id);
        if (guild is null) return;

        int selectionVersion = ++_guildSelectionVersion;
        _selectedDiscordGuild = guild;
        _activeGuildWorkspace = _guildWorkspaceStore.Load(guild.Id, guild.Name);
        _selectedGuildCaption!.Text = $"{guild.Name}  ·  {guild.MemberCount?.ToString("N0") ?? "üye sayısı yok"} üye";
        _serverPermissionCaption!.Text = guild.Owner || guild.IsAdministrator
            ? "Sunucu yönetimi ve rol yetkileri Discord'dan doğrulanacak."
            : guild.CanManageRoles ? "Rol yönetme izni algılandı." : "Rol yönetme iznin bulunmuyor.";
        LoadWorkspaceIntoControls();

        try
        {
            _selectedBotCaption!.Text = "YTMT BOT durumu kontrol ediliyor...";
            _selectedBotCaption.ForeColor = Palette.Yellow;
            _selectedBotStatus = await _discordOAuth!.GetBotStatusAsync(guild.Id);
            if (selectionVersion != _guildSelectionVersion) return;
            if (_selectedBotStatus.Installed)
            {
                _selectedBotCaption.Text = $"YTMT BOT bağlı · {_selectedBotStatus.Guild?.Name}";
                _selectedBotCaption.ForeColor = Palette.Green;
                _guildResources = await _discordOAuth.GetGuildResourcesAsync(guild.Id, _discordSession!.AccessToken);
                if (selectionVersion != _guildSelectionVersion) return;
                LoadGuildResourcesIntoControls();
            }
            else if (!_selectedBotStatus.ServiceAvailable)
            {
                _selectedBotCaption.Text = _selectedBotStatus.Error ?? "YTMT BOT API erişilemiyor.";
                _selectedBotCaption.ForeColor = Palette.Red;
                _guildResources = new DiscordGuildResources();
                LoadGuildResourcesIntoControls();
            }
            else
            {
                _selectedBotCaption.Text = "YTMT BOT bu sunucuda bulunamadı.";
                _selectedBotCaption.ForeColor = Palette.Yellow;
                _guildResources = new DiscordGuildResources();
                LoadGuildResourcesIntoControls();
            }
        }
        catch (Exception ex)
        {
            _selectedBotStatus = null;
            _selectedBotCaption!.Text = "Bot kontrolü başarısız: " + ex.Message;
            _selectedBotCaption.ForeColor = Palette.Red;
            _guildResources = new DiscordGuildResources();
            LoadGuildResourcesIntoControls();
        }
    }

    private void LoadGuildResourcesIntoControls()
    {
        _autoRoleList?.Items.Clear();
        if (_autoRoleList is not null && _activeGuildWorkspace is not null)
        {
            foreach (DiscordRoleInfo role in _guildResources.Roles.Where(r => r.Editable))
            {
                int index = _autoRoleList.Items.Add(new RoleListItem(role));
                if (_activeGuildWorkspace.AutoRoleIds.Contains(role.Id))
                    _autoRoleList.SetItemChecked(index, true);
            }
        }

        FillChannelCombo(_welcomeChannel, _guildResources.Channels, _activeGuildWorkspace?.WelcomeChannelId);
        FillChannelCombo(_logChannel, _guildResources.Channels, _activeGuildWorkspace?.LogChannelId);
        FillChannelCombo(_reactionChannel, _guildResources.Channels, null);
    }

    private void LoadWorkspaceIntoControls()
    {
        GuildWorkspace? workspace = _activeGuildWorkspace;
        if (workspace is null) return;
        _welcomeEnabled!.Checked = workspace.WelcomeEnabled;
        _welcomeMessage!.Text = workspace.WelcomeMessage;
        _antiSpamEnabled!.Checked = workspace.AntiSpamEnabled;
        _capsFilterEnabled!.Checked = workspace.CapsFilterEnabled;
        _profanityFilterEnabled!.Checked = workspace.ProfanityFilterEnabled;
        FillChannelCombo(_welcomeChannel, _guildResources.Channels, workspace.WelcomeChannelId);
        FillChannelCombo(_logChannel, _guildResources.Channels, workspace.LogChannelId);
        _reactionDraftList?.Items.Clear();
        foreach (ReactionRoleDraft draft in workspace.ReactionRoles)
        {
            string mode = draft.Mode == "new-message" ? "Yeni mesaj" : "Mevcut mesaj";
            _reactionDraftList?.Items.Add($"{mode} · {draft.Mappings.Count} tepki");
        }
        if (_draftStatus is not null)
            _draftStatus.Text = $"Taslak: {workspace.GuildName} · Son kayıt: {workspace.UpdatedAtUtc.ToLocalTime():g}";
    }

    private void FillChannelCombo(ComboBox? combo, List<DiscordChannelInfo> channels, string? selectedId)
    {
        if (combo is null) return;
        combo.DataSource = null;
        combo.DisplayMember = nameof(ChannelListItem.Display);
        combo.ValueMember = nameof(ChannelListItem.Id);
        combo.DataSource = channels.Select(c => new ChannelListItem(c.Id, "#" + c.Name)).ToList();
        if (!string.IsNullOrWhiteSpace(selectedId) && combo.Items.Count > 0)
            combo.SelectedValue = selectedId;
    }

    private void UpdateReactionModeControls()
    {
        bool isNew = _reactionMode?.SelectedIndex == 1;
        if (_reactionMessageId is not null) _reactionMessageId.Visible = !isNew;
        if (_reactionMessageContent is not null) _reactionMessageContent.Visible = isNew;
        if (_reactionMessageContent is not null) _reactionMessageContent.Height = 64;
        if (_reactionMessageId?.Parent is not null)
        {
            foreach (Control control in _reactionMessageId.Parent.Controls)
            {
                if (control.Name == "reactionContentLabel") control.Visible = isNew;
                if (control.Text == "Mevcut mesaj ID'si") control.Visible = !isNew;
            }
        }
    }

    private void AddReactionRoleDraft()
    {
        if (_activeGuildWorkspace is null || _selectedDiscordGuild is null)
        {
            ShowStudioHint("Önce bir sunucu seç.");
            return;
        }

        var draft = new ReactionRoleDraft
        {
            Mode = _reactionMode?.SelectedIndex == 1 ? "new-message" : "existing-message",
            ChannelId = _reactionChannel?.SelectedValue?.ToString() ?? string.Empty,
            MessageId = _reactionMessageId?.Text.Trim() ?? string.Empty,
            MessageContent = _reactionMessageContent?.Text.Trim() ?? string.Empty,
            Mappings = ReadReactionMappings()
        };

        if (draft.Mode == "existing-message" && !ulong.TryParse(draft.MessageId, out _))
        {
            ShowStudioHint("Mevcut mesaj yöntemi için sayısal bir mesaj ID'si gir.");
            return;
        }
        if (!ulong.TryParse(draft.ChannelId, out _))
        {
            ShowStudioHint("Önce bir metin kanalı seç.");
            return;
        }
        if (draft.Mode == "new-message" && string.IsNullOrWhiteSpace(draft.MessageContent))
        {
            ShowStudioHint("Yeni mesaj yöntemi için mesaj metni gir.");
            return;
        }
        if (draft.Mappings.Count == 0 || draft.Mappings.Any(m =>
                string.IsNullOrWhiteSpace(m.Emoji) || !ulong.TryParse(m.RoleId, out _)))
        {
            ShowStudioHint("En az bir geçerli emoji ve rol ID eşleştirmesi ekle.");
            return;
        }

        _activeGuildWorkspace.ReactionRoles.Add(draft);
        _reactionDraftList?.Items.Add($"{(draft.Mode == "new-message" ? "Yeni mesaj" : "Mevcut mesaj")} · {draft.Mappings.Count} tepki");
        _reactionMappingGrid?.Rows.Clear();
        if (_reactionMessageId is not null) _reactionMessageId.Clear();
        if (_reactionMessageContent is not null) _reactionMessageContent.Clear();
        SetDraftStatus("Mesaj rol taslağı eklendi. Canlı bota gönderilmedi.");
    }

    private List<ReactionRoleMappingDraft> ReadReactionMappings()
    {
        var mappings = new List<ReactionRoleMappingDraft>();
        if (_reactionMappingGrid is null) return mappings;
        foreach (DataGridViewRow row in _reactionMappingGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string emoji = Convert.ToString(row.Cells["Emoji"].Value)?.Trim() ?? string.Empty;
            string roleId = Convert.ToString(row.Cells["RoleId"].Value)?.Trim() ?? string.Empty;
            if (emoji.Length > 0 || roleId.Length > 0)
                mappings.Add(new ReactionRoleMappingDraft { Emoji = emoji, RoleId = roleId });
        }
        return mappings;
    }

    private void RemoveReactionRoleDraft()
    {
        int index = _reactionDraftList?.SelectedIndex ?? -1;
        if (index < 0 || _activeGuildWorkspace is null ||
            index >= _activeGuildWorkspace.ReactionRoles.Count) return;
        _activeGuildWorkspace.ReactionRoles.RemoveAt(index);
        _reactionDraftList!.Items.RemoveAt(index);
        SetDraftStatus("Seçili mesaj rol taslağı kaldırıldı.");
    }

    private void SaveGuildWorkspace()
    {
        if (_activeGuildWorkspace is null || _selectedDiscordGuild is null)
        {
            ShowStudioHint("Kaydetmek için önce bir sunucu seç.");
            return;
        }

        _activeGuildWorkspace.AutoRoleIds = _autoRoleList?.CheckedItems
            .Cast<RoleListItem>().Select(x => x.Role.Id).Distinct().ToList() ?? [];
        _activeGuildWorkspace.WelcomeEnabled = _welcomeEnabled?.Checked ?? false;
        _activeGuildWorkspace.WelcomeChannelId = _welcomeChannel?.SelectedValue?.ToString() ?? string.Empty;
        _activeGuildWorkspace.WelcomeMessage = _welcomeMessage?.Text ?? string.Empty;
        _activeGuildWorkspace.LogChannelId = _logChannel?.SelectedValue?.ToString() ?? string.Empty;
        _activeGuildWorkspace.AntiSpamEnabled = _antiSpamEnabled?.Checked ?? false;
        _activeGuildWorkspace.CapsFilterEnabled = _capsFilterEnabled?.Checked ?? false;
        _activeGuildWorkspace.ProfanityFilterEnabled = _profanityFilterEnabled?.Checked ?? false;

        try
        {
            _guildWorkspaceStore.Save(_activeGuildWorkspace);
            SetDraftStatus($"Taslak kaydedildi · {_activeGuildWorkspace.UpdatedAtUtc.ToLocalTime():g}");
        }
        catch (Exception ex)
        {
            ShowStudioHint("Taslak kaydedilemedi: " + ex.Message);
        }
    }

    private void ValidateGuildWorkspace()
    {
        if (_activeGuildWorkspace is null)
        {
            ShowStudioHint("Önce bir sunucu seç.");
            return;
        }

        SaveControlsToWorkspaceWithoutWriting();
        var problems = new List<string>();

        foreach (string roleId in _activeGuildWorkspace.AutoRoleIds)
        {
            DiscordRoleInfo? role = _guildResources.Roles.FirstOrDefault(r => r.Id == roleId);
            if (role is null) problems.Add($"Oto rol {roleId}: bot sunucuda rolü göremiyor.");
            else if (!role.Editable) problems.Add($"Oto rol {role.Name}: botun rol hiyerarşisi bu role izin vermiyor.");
        }

        if (_activeGuildWorkspace.WelcomeEnabled)
        {
            if (string.IsNullOrWhiteSpace(_activeGuildWorkspace.WelcomeChannelId))
                problems.Add("Karşılama açık ama kanal seçilmemiş.");
            if (string.IsNullOrWhiteSpace(_activeGuildWorkspace.WelcomeMessage))
                problems.Add("Karşılama mesajı boş.");
        }

        foreach (ReactionRoleDraft draft in _activeGuildWorkspace.ReactionRoles)
        {
            if (!ulong.TryParse(draft.ChannelId, out _))
                problems.Add("Mesaj rol taslağında geçerli kanal ID'si yok.");
            if (draft.Mode == "existing-message" && !ulong.TryParse(draft.MessageId, out _))
                problems.Add("Mevcut mesaj yönteminde mesaj ID'si eksik.");
            if (draft.Mode == "new-message" && string.IsNullOrWhiteSpace(draft.MessageContent))
                problems.Add("Yeni mesaj yöntemi için mesaj metni eksik.");
            if (draft.Mappings.Count == 0)
                problems.Add("Mesaj rol taslağı en az bir emoji/rol eşleştirmesi içermeli.");
            foreach (ReactionRoleMappingDraft map in draft.Mappings)
            {
                if (!ulong.TryParse(map.RoleId, out _))
                    problems.Add($"'{map.Emoji}' tepkisi için rol ID'si geçersiz.");
                else if (_guildResources.Roles.All(r => r.Id != map.RoleId || !r.Editable))
                    problems.Add($"'{map.Emoji}' eşleştirmesindeki rol bot tarafından yönetilemiyor.");
            }
        }

        string result = problems.Count == 0
            ? "Kontrol tamamlandı. Taslakta görünen yapılandırma hatası bulunamadı."
            : "Kontrol tamamlandı. Düzeltilmesi gerekenler:\n\n• " + string.Join("\n• ", problems);
        MessageBox.Show(this, result, "Yapılandırma kontrolü",
            MessageBoxButtons.OK, problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        _guildWorkspaceStore.AppendAudit(_activeGuildWorkspace.GuildId,
            _activeGuildWorkspace.GuildName, "workspace_validated",
            problems.Count == 0 ? "validation_ok" : $"validation_warnings:{problems.Count}");
    }

    private void SaveControlsToWorkspaceWithoutWriting()
    {
        if (_activeGuildWorkspace is null) return;
        _activeGuildWorkspace.AutoRoleIds = _autoRoleList?.CheckedItems
            .Cast<RoleListItem>().Select(x => x.Role.Id).Distinct().ToList() ?? [];
        _activeGuildWorkspace.WelcomeEnabled = _welcomeEnabled?.Checked ?? false;
        _activeGuildWorkspace.WelcomeChannelId = _welcomeChannel?.SelectedValue?.ToString() ?? string.Empty;
        _activeGuildWorkspace.WelcomeMessage = _welcomeMessage?.Text ?? string.Empty;
        _activeGuildWorkspace.LogChannelId = _logChannel?.SelectedValue?.ToString() ?? string.Empty;
        _activeGuildWorkspace.AntiSpamEnabled = _antiSpamEnabled?.Checked ?? false;
        _activeGuildWorkspace.CapsFilterEnabled = _capsFilterEnabled?.Checked ?? false;
        _activeGuildWorkspace.ProfanityFilterEnabled = _profanityFilterEnabled?.Checked ?? false;
    }

    private void ExportGuildWorkspace()
    {
        if (_activeGuildWorkspace is null)
        {
            ShowStudioHint("Dışa aktarmak için önce bir sunucu seç.");
            return;
        }
        SaveControlsToWorkspaceWithoutWriting();
        using var dialog = new SaveFileDialog
        {
            Title = "Sunucu yapılandırmasını dışa aktar",
            Filter = "JSON dosyası (*.json)|*.json",
            FileName = $"ytmt-guild-{_activeGuildWorkspace.GuildId}.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _guildWorkspaceStore.Export(_activeGuildWorkspace, dialog.FileName);
            SetDraftStatus("JSON dosyası dışa aktarıldı.");
        }
        catch (Exception ex) { ShowStudioHint("Dışa aktarma başarısız: " + ex.Message); }
    }

    private void ImportGuildWorkspace()
    {
        if (_selectedDiscordGuild is null)
        {
            ShowStudioHint("İçe aktarmak için önce bir sunucu seç.");
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "Sunucu taslağı içe aktar",
            Filter = "JSON dosyası (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _activeGuildWorkspace = _guildWorkspaceStore.Import(dialog.FileName, _selectedDiscordGuild.Id);
            LoadWorkspaceIntoControls();
            LoadGuildResourcesIntoControls();
            SetDraftStatus("Taslak içe aktarıldı. Kaydetmek için Taslağı Kaydet'e bas.");
        }
        catch (Exception ex) { ShowStudioHint("İçe aktarma başarısız: " + ex.Message); }
    }

    private void OpenGuildAuditLog()
    {
        try
        {
            string path = _guildWorkspaceStore.AuditLogPath;
            if (!File.Exists(path)) File.WriteAllText(path, string.Empty);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowStudioHint("Kayıt dosyası açılamadı: " + ex.Message); }
    }

    private void OpenBotInvite()
    {
        if (_selectedDiscordGuild is null || string.IsNullOrWhiteSpace(_settings.DiscordClientId))
        {
            ShowStudioHint("Sunucu seç ve Discord Client ID değerini kaydet.");
            return;
        }
        try
        {
            string url = DiscordOAuthService.BuildBotInviteUrl(_settings.DiscordClientId, _selectedDiscordGuild.Id);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _guildWorkspaceStore.AppendAudit(_selectedDiscordGuild.Id, _selectedDiscordGuild.Name,
                "bot_invite_opened", "YTMT BOT davet bağlantısı açıldı.");
        }
        catch (Exception ex) { ShowStudioHint("Davet bağlantısı açılamadı: " + ex.Message); }
    }

    private void SetDiscordConnectionText(string text, Color color)
    {
        if (_discordConnectionStatus is null || _discordConnectionStatus.IsDisposed) return;
        if (_discordConnectionStatus.InvokeRequired)
        {
            _discordConnectionStatus.BeginInvoke(new Action(() => SetDiscordConnectionText(text, color)));
            return;
        }
        _discordConnectionStatus.Text = text;
        _discordConnectionStatus.ForeColor = color;
    }

    private void SetDraftStatus(string text)
    {
        if (_draftStatus is null || _draftStatus.IsDisposed) return;
        _draftStatus.Text = text;
    }

    private void ShowStudioHint(string message)
    {
        MessageBox.Show(this, message, "YTMT-STUDIO", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private TabPage MakeTab(string text)
    {
        return new TabPage(text)
        {
            BackColor = Palette.Background,
            ForeColor = Palette.Text,
            Padding = new Padding(4)
        };
    }

    private Label MakeStudioLabel(string text, Point location)
    {
        return new Label
        {
            Text = text,
            Location = location,
            AutoSize = true,
            ForeColor = Palette.Text,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
    }

    private Button MakeStudioButton(string text, Point location, int width, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = new Size(width, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Palette.Blue : Palette.Surface,
            ForeColor = primary ? Color.White : Palette.Text,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Palette.Border;
        button.FlatAppearance.MouseOverBackColor = Palette.SurfaceHover;
        return button;
    }

    private CheckBox MakeStudioCheck(string text, Point location)
    {
        return new CheckBox
        {
            Text = text,
            Location = location,
            AutoSize = true,
            ForeColor = Palette.Text
        };
    }

    private sealed class GuildGridRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MemberCount { get; set; } = string.Empty;
        public string PermissionSummary { get; set; } = string.Empty;
    }

    private sealed record RoleListItem(DiscordRoleInfo Role)
    {
        public override string ToString() =>
            $"{Role.Name}  ·  {Role.Id}" + (Role.Editable ? string.Empty : "  [bot erişemiyor]");
    }

    private sealed record ChannelListItem(string Id, string Display);
}
