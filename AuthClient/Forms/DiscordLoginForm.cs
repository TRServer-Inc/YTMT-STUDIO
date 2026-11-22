using System.Drawing;
using System.Globalization;
using YTMT_STUDIO.Models;
using YTMT_STUDIO.Services;

namespace YTMT_STUDIO.AuthClient.Forms;

public sealed class DiscordLoginForm : Form
{
    private static readonly Color Background = Color.FromArgb(10, 14, 22);
    private static readonly Color Surface = Color.FromArgb(19, 26, 38);
    private static readonly Color Border = Color.FromArgb(43, 55, 75);
    private static readonly Color Primary = Color.FromArgb(88, 166, 255);
    private static readonly Color Foreground = Color.FromArgb(237, 242, 250);
    private static readonly Color Muted = Color.FromArgb(145, 158, 180);
    private static readonly Color Danger = Color.FromArgb(255, 118, 118);

    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly TextBox _clientId = new();
    private readonly Button _loginButton = new();
    private readonly Label _status = new();
    private bool _busy;

    public DiscordSession? Session { get; private set; }
    public DiscordOAuthService? OAuthService { get; private set; }

    public DiscordLoginForm(SettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;

        Text = "YTMT-STUDIO | Discord ile Giriş";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimumSize = new Size(520, 430);
        Size = new Size(560, 460);
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 9.5F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildInterface();
    }

    private void BuildInterface()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(34)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        Controls.Add(card);

        var brand = new Label
        {
            Text = "YTMT-STUDIO",
            Location = new Point(34, 28),
            AutoSize = true,
            ForeColor = Foreground,
            Font = new Font("Segoe UI Semibold", 17, FontStyle.Bold)
        };
        var title = new Label
        {
            Text = "Discord ile giriş yap",
            Location = new Point(36, 94),
            AutoSize = true,
            ForeColor = Foreground,
            Font = new Font("Segoe UI Semibold", 21, FontStyle.Bold)
        };
        var description = new Label
        {
            Text = "Studio hesabı veya Render kimlik doğrulama servisi gerekmez.\nDiscord hesabınla güvenli OAuth2 bağlantısı kurulur.",
            Location = new Point(38, 140),
            Size = new Size(430, 48),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5F)
        };
        var clientLabel = new Label
        {
            Text = "DISCORD APPLICATION / CLIENT ID",
            Location = new Point(38, 212),
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8, FontStyle.Bold)
        };
        _clientId.Location = new Point(38, 238);
        _clientId.Size = new Size(430, 36);
        _clientId.BorderStyle = BorderStyle.FixedSingle;
        _clientId.BackColor = Background;
        _clientId.ForeColor = Foreground;
        _clientId.Font = new Font("Segoe UI", 10);
        _clientId.PlaceholderText = "Discord Developer Portal'daki Client ID";
        _clientId.Text = _settings.DiscordClientId;

        _loginButton.Text = "Discord ile Giriş Yap";
        _loginButton.Location = new Point(38, 294);
        _loginButton.Size = new Size(430, 48);
        _loginButton.FlatStyle = FlatStyle.Flat;
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.BackColor = Primary;
        _loginButton.ForeColor = Color.White;
        _loginButton.Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        _loginButton.Cursor = Cursors.Hand;
        _loginButton.Click += async (_, _) => await LoginAsync();

        _status.Location = new Point(38, 354);
        _status.Size = new Size(430, 38);
        _status.ForeColor = Muted;
        _status.Font = new Font("Segoe UI", 8.5F);
        _status.TextAlign = ContentAlignment.MiddleCenter;

        card.Controls.AddRange([brand, title, description, clientLabel, _clientId, _loginButton, _status]);
        AcceptButton = _loginButton;
    }

    private async Task LoginAsync()
    {
        if (_busy) return;

        string clientId = _clientId.Text.Trim();
        if (!ulong.TryParse(clientId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            SetStatus("Geçerli bir sayısal Discord Client ID gir.", Danger);
            return;
        }

        SetBusy(true);
        SetStatus("Discord tarayıcı penceresi açılıyor...", Muted);

        try
        {
            _settings.DiscordClientId = clientId;
            _settingsService.Save(_settings);

            OAuthService?.Dispose();
            OAuthService = new DiscordOAuthService(_settings.DiscordClientId, _settings.BotApiUrl);
            Session = await OAuthService.SignInAsync();

            SetStatus("Discord bağlantısı tamamlandı.", Primary);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            OAuthService?.Dispose();
            OAuthService = null;
            SetStatus(ex.Message, Danger);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _clientId.Enabled = !busy;
        _loginButton.Enabled = !busy;
        _loginButton.Text = busy ? "Discord onayı bekleniyor..." : "Discord ile Giriş Yap";
        UseWaitCursor = busy;
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}