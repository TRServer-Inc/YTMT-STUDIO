using System.Drawing;
using System.Windows.Forms;
using YTMT_STUDIO.Services;
using YTMT_STUDIO.AuthClient.Models;
using YTMT_STUDIO.Models;

namespace YTMT_STUDIO;

public sealed partial class MainForm
{
    // Render infrastructure controls are available only to accounts with the owner role.
    private bool IsRenderControlOwner => _authSession.User.CanManageRender;

    private readonly RenderCredentialStore _renderCredentialStore = new();
    private Panel? _botControlPage;
    private Label? _renderServiceStatus;
    private Label? _renderServiceDetails;
    private Label? _renderCredentialStatus;
    private Label? _renderActionStatus;
    private TextBox? _renderServiceIdInput;
    private TextBox? _renderApiKeyInput;
    private Button? _renderResumeButton;
    private Button? _renderSuspendButton;
    private Button? _renderRestartButton;
    private Button? _renderRefreshButton;
    private Button? _renderLogsRefreshButton;
    private TextBox? _renderLogsText;
    private Label? _renderLogsStatus;
    private bool _renderLogsBusy;
    private bool _renderBusy;
    private bool? _renderIsSuspended;

    private Panel CreateBotControlPage()
    {
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Palette.Background,
            AutoScroll = true
        };

        var intro = new Label
        {
            Text = "Render üzerindeki YTMT BOT hizmetini yönet. Bu işlemler yalnızca bot sürecini değil, aynı Render web hizmetindeki API uçlarını da etkiler.",
            ForeColor = Palette.Muted,
            Location = new Point(2, 4),
            Size = new Size(720, 42),
            Font = new Font("Segoe UI", 9F)
        };

        var statusCard = CreateBotControlCard(58, 206);
        var statusTitle = CreateBotControlCardTitle("RENDER HİZMET DURUMU", 20, 16);

        _renderServiceStatus = new Label
        {
            Text = "Yapılandırma bekleniyor",
            ForeColor = Palette.Yellow,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
            Location = new Point(22, 54),
            AutoSize = true
        };

        _renderServiceDetails = new Label
        {
            Text = "Render Service ID ve API anahtarını aşağıdaki ayar bölümüne gir.",
            ForeColor = Palette.Muted,
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(22, 82),
            Size = new Size(680, 38)
        };

        _renderRefreshButton = CreateSmallButton("Durumu Yenile", 132);
        _renderRefreshButton.Location = new Point(22, 137);
        _renderRefreshButton.Click += async (_, _) => await RefreshRenderServiceAsync();

        _renderResumeButton = CreateSmallButton("Başlat", 112);
        _renderResumeButton.Location = new Point(166, 137);
        _renderResumeButton.BackColor = Palette.Green;
        _renderResumeButton.Click += async (_, _) => await ExecuteRenderActionAsync("resume");

        _renderSuspendButton = CreateSmallButton("Durdur", 112);
        _renderSuspendButton.Location = new Point(290, 137);
        _renderSuspendButton.BackColor = Palette.Red;
        _renderSuspendButton.Click += async (_, _) => await ExecuteRenderActionAsync("suspend");

        _renderRestartButton = CreateSmallButton("Yeniden Başlat", 142);
        _renderRestartButton.Location = new Point(414, 137);
        _renderRestartButton.Click += async (_, _) => await ExecuteRenderActionAsync("restart");

        _renderActionStatus = new Label
        {
            Text = "İşlem geçmişi bu panelde gösterilir.",
            ForeColor = Palette.Muted,
            Location = new Point(22, 180),
            Size = new Size(680, 18),
            Font = new Font("Segoe UI", 8F)
        };

        statusCard.Controls.AddRange([
            statusTitle,
            _renderServiceStatus,
            _renderServiceDetails,
            _renderRefreshButton,
            _renderResumeButton,
            _renderSuspendButton,
            _renderRestartButton,
            _renderActionStatus
        ]);

        var configCard = CreateBotControlCard(276, 260);
        var configTitle = CreateBotControlCardTitle("RENDER BAĞLANTISI", 20, 16);

        var serviceIdLabel = MakeSettingLabel("Render Service ID (srv-...)", 22, 55);
        _renderServiceIdInput = MakeSettingInput(
            _settings.RenderServiceId,
            22,
            78,
            690);
        _renderServiceIdInput.PlaceholderText = "srv-xxxxxxxxxxxxxxxxxxxx";
        _renderServiceIdInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var keyLabel = MakeSettingLabel("Render API anahtarı (yalnızca bu bilgisayarda şifreli saklanır)", 22, 125);
        _renderApiKeyInput = MakeSettingInput(string.Empty, 22, 148, 690);
        _renderApiKeyInput.UseSystemPasswordChar = true;
        _renderApiKeyInput.PlaceholderText = "Yeni anahtar gir; mevcut kayıtlı anahtar değişmeden kalsın diye boş bırakabilirsin";
        _renderApiKeyInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _renderCredentialStatus = new Label
        {
            Text = _renderCredentialStore.HasSavedKey
                ? "● Şifreli Render anahtarı kayıtlı."
                : "● Henüz Render anahtarı kaydedilmedi.",
            ForeColor = _renderCredentialStore.HasSavedKey ? Palette.Green : Palette.Yellow,
            Location = new Point(22, 185),
            Size = new Size(690, 20),
            Font = new Font("Segoe UI", 8F)
        };

        var saveConfig = CreateSmallButton("Ayarları Kaydet", 150);
        saveConfig.Location = new Point(22, 215);
        saveConfig.Click += async (_, _) => await SaveRenderSettingsAsync();

        var clearKey = CreateSmallButton("Anahtarı Sil", 120);
        clearKey.Location = new Point(184, 215);
        clearKey.BackColor = Color.FromArgb(93, 48, 48);
        clearKey.Click += (_, _) => ClearRenderCredentials();

        configCard.Controls.AddRange([
            configTitle,
            serviceIdLabel,
            _renderServiceIdInput,
            keyLabel,
            _renderApiKeyInput,
            _renderCredentialStatus,
            saveConfig,
            clearKey
        ]);

        var notesCard = CreateBotControlCard(554, 138);
        var notesTitle = CreateBotControlCardTitle("ÖNEMLİ NOTLAR", 20, 16);
        var notes = new Label
        {
            Text = "• Durdur: Render hizmetini askıya alır; bot ve aynı hizmetteki API adresleri kapanır.\r\n• Başlat: Askıya alınmış hizmeti yeniden sürdürür.\r\n• Yeniden başlat: Render aynı sürümden yeni bir örnek oluşturup hizmeti yeniler.\r\n• Render anahtarı settings.json'a yazılmaz; Windows DPAPI ile mevcut Windows kullanıcısına özel şifrelenir.",
            ForeColor = Palette.Muted,
            Location = new Point(22, 48),
            Size = new Size(700, 82),
            Font = new Font("Segoe UI", 8.2F)
        };
        notesCard.Controls.AddRange([notesTitle, notes]);

        var logsCard = CreateBotControlCard(708, 244);
        var logsTitle = CreateBotControlCardTitle("SON UYGULAMA LOGLARI", 20, 16);
        _renderLogsRefreshButton = CreateSmallButton("Logları Yenile", 132);
        _renderLogsRefreshButton.Location = new Point(590, 9);
        _renderLogsRefreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _renderLogsRefreshButton.Click += async (_, _) => await RefreshRenderLogsAsync();

        _renderLogsStatus = new Label
        {
            Text = "Render uygulama loglarını görmek için Logları Yenile düğmesine bas.",
            ForeColor = Palette.Muted,
            Location = new Point(22, 43),
            Size = new Size(690, 18),
            Font = new Font("Segoe UI", 8F)
        };

        _renderLogsText = new TextBox
        {
            Location = new Point(22, 66),
            Size = new Size(690, 158),
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BackColor = Color.FromArgb(10, 13, 18),
            ForeColor = Palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5F),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        logsCard.Controls.AddRange([logsTitle, _renderLogsRefreshButton, _renderLogsStatus, _renderLogsText]);

        root.Controls.AddRange([intro, statusCard, configCard, notesCard, logsCard]);

        root.Resize += (_, _) =>
        {
            int width = Math.Max(500, root.ClientSize.Width - 10);
            statusCard.Width = width;
            configCard.Width = width;
            notesCard.Width = width;
            logsCard.Width = width;

            if (_renderLogsText is not null)
                _renderLogsText.Width = width - 44;
            if (_renderLogsStatus is not null)
                _renderLogsStatus.Width = width - 44;
            if (_renderLogsRefreshButton is not null)
                _renderLogsRefreshButton.Left = width - _renderLogsRefreshButton.Width - 22;

            if (_renderServiceDetails is not null)
                _renderServiceDetails.Width = width - 44;
            if (_renderActionStatus is not null)
                _renderActionStatus.Width = width - 44;
            if (_renderServiceIdInput is not null)
                _renderServiceIdInput.Width = width - 44;
            if (_renderApiKeyInput is not null)
                _renderApiKeyInput.Width = width - 44;
        };

        UpdateRenderCredentialStatus();
        UpdateRenderActionButtons();
        return root;
    }

    private static Panel CreateBotControlCard(int top, int height) => new()
    {
        Location = new Point(0, top),
        Size = new Size(760, height),
        BackColor = Palette.Surface,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
    };

    private static Label CreateBotControlCardTitle(string text, int left, int top) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(left, top),
        ForeColor = Palette.Text,
        Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
    };

    private async Task SaveRenderSettingsAsync()
    {
        if (_renderServiceIdInput is null || _renderApiKeyInput is null)
            return;

        string serviceId = _renderServiceIdInput.Text.Trim();
        string newApiKey = _renderApiKeyInput.Text.Trim();

        if (!RenderControlService.IsValidServiceId(serviceId))
        {
            MessageBox.Show(
                this,
                "Render Service ID geçerli görünmüyor. Render panelindeki srv-... kimliğini gir.",
                "Render ayarları",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(newApiKey))
                _renderCredentialStore.Save(newApiKey);

            if (!_renderCredentialStore.HasSavedKey)
            {
                MessageBox.Show(
                    this,
                    "Devam etmek için Render API anahtarı girmen gerekiyor.",
                    "Render ayarları",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _settings = new AppSettings
            {
                BotApiUrl = _settings.BotApiUrl,
                TerminalUrl = _settings.TerminalUrl,
                RefreshIntervalSeconds = _settings.RefreshIntervalSeconds,
                RenderServiceId = serviceId
            };
            _settingsService.Save(_settings);

            _renderApiKeyInput.Clear();
            UpdateRenderCredentialStatus();
            AppendRenderActionLog("Render bağlantı ayarları kaydedildi. API anahtarı DPAPI ile şifrelendi.");
            await RefreshRenderServiceAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Render ayarları kaydedilemedi: " + ex.Message,
                "YTMT-STUDIO",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ClearRenderCredentials()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Kaydedilmiş Render API anahtarı bu Windows kullanıcısından silinsin mi?",
            "Render anahtarını sil",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        _renderCredentialStore.Clear();
        _renderApiKeyInput?.Clear();
        UpdateRenderCredentialStatus();
        UpdateRenderActionButtons();
        AppendRenderActionLog("Kaydedilmiş Render API anahtarı silindi.");
        SetRenderServiceStatus("Render anahtarı kayıtlı değil", Palette.Yellow,
            "Bot yönetimi için önce Render API anahtarını yeniden kaydet.");
    }

    private async Task RefreshRenderServiceAsync()
    {
        if (!IsRenderControlOwner || IsDisposed || Disposing)
            return;

        string serviceId = _settings.RenderServiceId.Trim();
        if (!RenderControlService.IsValidServiceId(serviceId))
        {
            SetRenderServiceStatus("Render bağlantısı yapılandırılmadı", Palette.Yellow,
                "Service ID ve API anahtarını aşağıdaki bağlantı kartına girip Ayarları Kaydet düğmesine bas.");
            UpdateRenderActionButtons();
            return;
        }

        string? apiKey;
        try
        {
            apiKey = _renderCredentialStore.Load();
        }
        catch (Exception ex)
        {
            SetRenderServiceStatus("Render anahtarı okunamadı", Palette.Red, ex.Message);
            UpdateRenderActionButtons();
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetRenderServiceStatus("Render API anahtarı eksik", Palette.Yellow,
                "Aşağıdaki alana Render API anahtarını gir ve kaydet.");
            UpdateRenderActionButtons();
            return;
        }

        SetRenderBusy(true);
        SetRenderServiceStatus("Render durumu sorgulanıyor...", Palette.Yellow,
            "Render API ile hizmet bilgisi alınıyor.");

        try
        {
            using var render = new RenderControlService(apiKey);
            RenderServiceInfo info = await render.GetServiceAsync(serviceId);

            _renderIsSuspended = info.IsSuspended;
            string status = info.IsSuspended ? "HİZMET ASKIYA ALINMIŞ" : "HİZMET ASKIYA ALINMAMIŞ";
            Color statusColor = info.IsSuspended ? Palette.Red : Palette.Green;
            string updated = info.UpdatedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "bilinmiyor";
            string urlLine = string.IsNullOrWhiteSpace(info.ServiceUrl) ? string.Empty : $"\nURL: {info.ServiceUrl}";

            SetRenderServiceStatus(status, statusColor,
                $"{info.Name} · {info.Type} · Son güncelleme: {updated}{urlLine}\nBu alan Render askıya alma durumunu gösterir; Discord bağlantısını Genel Bakış sayfasından kontrol et.");
            AppendRenderActionLog($"Render durumu yenilendi: {info.Name} / {(info.IsSuspended ? "suspended" : "not_suspended")}.");
        }
        catch (Exception ex)
        {
            _renderIsSuspended = null;
            SetRenderServiceStatus("Render durumuna ulaşılamadı", Palette.Red, ex.Message);
            AppendRenderActionLog("Durum sorgusu başarısız: " + ex.Message);
        }
        finally
        {
            SetRenderBusy(false);
            UpdateRenderActionButtons();
        }
    }

    private async Task RefreshRenderLogsAsync()
    {
        if (_renderLogsBusy || !IsRenderControlOwner || IsDisposed || Disposing)
            return;

        string serviceId = _settings.RenderServiceId.Trim();
        if (!RenderControlService.IsValidServiceId(serviceId) || !_renderCredentialStore.HasSavedKey)
        {
            if (_renderLogsStatus is not null)
                _renderLogsStatus.Text = "Önce Render Service ID ve API anahtarını kaydet.";
            return;
        }

        string? apiKey;
        try
        {
            apiKey = _renderCredentialStore.Load();
        }
        catch (Exception ex)
        {
            if (_renderLogsStatus is not null)
                _renderLogsStatus.Text = "Render anahtarı okunamadı: " + ex.Message;
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        _renderLogsBusy = true;
        if (_renderLogsRefreshButton is not null)
        {
            _renderLogsRefreshButton.Enabled = false;
            _renderLogsRefreshButton.Text = "Yükleniyor...";
        }
        if (_renderLogsStatus is not null)
            _renderLogsStatus.Text = "Render'dan son uygulama logları alınıyor...";

        try
        {
            using var render = new RenderControlService(apiKey);
            RenderServiceInfo service = await render.GetServiceAsync(serviceId);
            IReadOnlyList<RenderLogEntry> logs = await render.GetRecentApplicationLogsAsync(
                service.OwnerId, serviceId, 50);

            if (_renderLogsText is not null)
            {
                _renderLogsText.Text = logs.Count == 0
                    ? "Bu hizmet için son zaman aralığında uygulama logu bulunamadı."
                    : string.Join(Environment.NewLine, logs.Select(log =>
                    {
                        string time = log.Timestamp?.ToLocalTime().ToString("HH:mm:ss") ?? "--:--:--";
                        string level = log.Level.ToUpperInvariant();
                        string message = log.Message.Replace("\r", " ").Replace("\n", " ");
                        return $"[{time}] [{level}] {message}";
                    }));
                _renderLogsText.SelectionStart = _renderLogsText.TextLength;
                _renderLogsText.ScrollToCaret();
            }

            if (_renderLogsStatus is not null)
                _renderLogsStatus.Text = $"Son 50 uygulama kaydı · {logs.Count} kayıt gösteriliyor · {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            if (_renderLogsText is not null)
                _renderLogsText.Clear();
            if (_renderLogsStatus is not null)
                _renderLogsStatus.Text = "Loglar alınamadı: " + ex.Message;
        }
        finally
        {
            _renderLogsBusy = false;
            if (_renderLogsRefreshButton is not null)
            {
                _renderLogsRefreshButton.Enabled = true;
                _renderLogsRefreshButton.Text = "Logları Yenile";
            }
        }
    }

    private async Task ExecuteRenderActionAsync(string action)
    {
        if (_renderBusy || !IsRenderControlOwner)
            return;

        string serviceId = _settings.RenderServiceId.Trim();
        if (!RenderControlService.IsValidServiceId(serviceId))
        {
            await RefreshRenderServiceAsync();
            return;
        }

        if (_renderIsSuspended is null)
        {
            await RefreshRenderServiceAsync();
            if (_renderIsSuspended is null)
                return;
        }

        if (action == "resume" && _renderIsSuspended != true)
            return;
        if (action == "suspend" && _renderIsSuspended != false)
            return;
        if (action == "restart" && _renderIsSuspended != false)
            return;

        string title;
        string message;
        switch (action)
        {
            case "resume":
                title = "Render hizmetini başlat";
                message = "Askıya alınmış YTMT BOT Render hizmeti yeniden sürdürülecek. Hizmetin açılması biraz sürebilir. Devam edilsin mi?";
                break;
            case "suspend":
                title = "Render hizmetini durdur";
                message = "YTMT BOT'un bulunduğu Render web hizmeti askıya alınacak. Bot ve aynı hizmetteki API adresleri erişilemez olacak. Devam edilsin mi?";
                break;
            case "restart":
                title = "Render hizmetini yeniden başlat";
                message = "Render hizmeti aynı kod ve yapılandırmayla yeniden başlatılacak. Devam edilsin mi?";
                break;
            default:
                return;
        }

        DialogResult answer = MessageBox.Show(
            this, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        string? apiKey;
        try
        {
            apiKey = _renderCredentialStore.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Render anahtarı", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await RefreshRenderServiceAsync();
            return;
        }

        SetRenderBusy(true);
        SetRenderServiceStatus("İşlem gönderiliyor...", Palette.Yellow,
            "İstek Render API'ye iletiliyor.");

        try
        {
            using var render = new RenderControlService(apiKey);
            switch (action)
            {
                case "resume":
                    await render.ResumeServiceAsync(serviceId);
                    AppendRenderActionLog("Başlatma isteği Render tarafından kabul edildi.");
                    break;
                case "suspend":
                    await render.SuspendServiceAsync(serviceId);
                    AppendRenderActionLog("Durdurma/askıya alma isteği Render tarafından kabul edildi.");
                    break;
                case "restart":
                    await render.RestartServiceAsync(serviceId);
                    AppendRenderActionLog("Yeniden başlatma isteği Render tarafından kabul edildi.");
                    break;
            }

            SetRenderServiceStatus("İstek kabul edildi", Palette.Green,
                "Render işlemi arka planda tamamlıyor. Birkaç saniye sonra durumu yenile.");
        }
        catch (Exception ex)
        {
            SetRenderServiceStatus("Render işlemi başarısız", Palette.Red, ex.Message);
            AppendRenderActionLog("İşlem başarısız: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Render işlemi başarısız", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRenderBusy(false);
            UpdateRenderActionButtons();
        }

        await Task.Delay(1500);
        await RefreshRenderServiceAsync();
    }

    private void SetRenderBusy(bool busy)
    {
        _renderBusy = busy;
        if (_renderRefreshButton is not null)
            _renderRefreshButton.Enabled = !busy;
        UpdateRenderActionButtons();
    }

    private void UpdateRenderActionButtons()
    {
        bool configured = RenderControlService.IsValidServiceId(_settings.RenderServiceId) &&
                          _renderCredentialStore.HasSavedKey && IsRenderControlOwner;

        if (_renderResumeButton is not null)
            _renderResumeButton.Enabled = configured && !_renderBusy && _renderIsSuspended == true;
        if (_renderSuspendButton is not null)
            _renderSuspendButton.Enabled = configured && !_renderBusy && _renderIsSuspended == false;
        if (_renderRestartButton is not null)
            _renderRestartButton.Enabled = configured && !_renderBusy && _renderIsSuspended == false;
        if (_renderRefreshButton is not null)
            _renderRefreshButton.Enabled = configured && !_renderBusy;
    }

    private void UpdateRenderCredentialStatus()
    {
        if (_renderCredentialStatus is null)
            return;

        bool saved = _renderCredentialStore.HasSavedKey;
        _renderCredentialStatus.Text = saved
            ? "● Render API anahtarı bu Windows kullanıcısı için DPAPI ile şifreli kayıtlı."
            : "● Henüz Render API anahtarı kaydedilmedi.";
        _renderCredentialStatus.ForeColor = saved ? Palette.Green : Palette.Yellow;
    }

    private void SetRenderServiceStatus(string title, Color color, string details)
    {
        if (_renderServiceStatus is not null)
        {
            _renderServiceStatus.Text = title;
            _renderServiceStatus.ForeColor = color;
        }

        if (_renderServiceDetails is not null)
            _renderServiceDetails.Text = details;
    }

    private void AppendRenderActionLog(string message)
    {
        if (_renderActionStatus is null)
            return;

        _renderActionStatus.Text = $"{DateTime.Now:HH:mm:ss} · {message}";
    }
}